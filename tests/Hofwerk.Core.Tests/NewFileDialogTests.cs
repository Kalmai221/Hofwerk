using Hofwerk.Core;
using Hofwerk.Core.Omsi;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

public class NewFileDialogTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-newdialog-" + Guid.NewGuid().ToString("N"));

    public NewFileDialogTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static MapData Sample(string name) => new()
    {
        Name = name,
        BusStops = ["One", "Two"],
        Destinations = ["Town"],
        Trips = [new MapTrip("A", "Town", "7", ["One", "Two"])],
    };

    static async Task WaitUntil(Func<bool> done)
    {
        for (int i = 0; i < 200 && !done(); i++) await Task.Delay(25);
        Assert.True(done());
    }

    [Fact]
    public void An_empty_depot_can_be_made_straight_away()
    {
        var dialog = new NewFileViewModel([new OmsiMap("Cotterell", "x")]);

        Assert.False(dialog.FromMap);
        Assert.True(dialog.CanContinue);
        Assert.Equal("", dialog.Summary);
        Assert.Null(dialog.ToPlan().Map);
        Assert.Equal("New depot", dialog.SuggestedFileName);
    }

    [Fact]
    public async Task Choosing_a_map_reads_it_and_says_what_it_found()
    {
        var dialog = new NewFileViewModel([new OmsiMap("Cotterell", "c"), new OmsiMap("Other", "o")], folder => Sample(Path.GetFileName(folder)));
        dialog.FromMap = true;
        Assert.False(dialog.CanContinue);                       // no map chosen yet
        Assert.Equal("Choose a map.", dialog.Summary);

        dialog.SelectedMap = dialog.Maps[0];
        Assert.Equal("Reading the map…", dialog.Summary);
        await WaitUntil(() => !dialog.IsReading);

        Assert.Equal("Found 2 bus stops, 1 destination, 1 route.", dialog.Summary);
        Assert.True(dialog.CanContinue);
        Assert.Equal("Cotterell", dialog.SuggestedFileName);
        Assert.Equal("c", dialog.ToPlan().Map!.Name);
    }

    [Fact]
    public async Task A_map_without_timetables_cannot_be_imported()
    {
        var dialog = new NewFileViewModel([new OmsiMap("Bare", "b")], _ => new MapData { Name = "Bare" });
        dialog.FromMap = true;
        dialog.SelectedMap = dialog.Maps[0];
        await WaitUntil(() => !dialog.IsReading);

        Assert.Contains("no timetable data", dialog.Summary);
        Assert.False(dialog.CanContinue);
    }

    [Fact]
    public async Task At_least_one_part_has_to_be_ticked_and_the_choice_goes_into_the_plan()
    {
        var dialog = new NewFileViewModel([new OmsiMap("A", "a")], _ => Sample("A"));
        dialog.FromMap = true;
        dialog.SelectedMap = dialog.Maps[0];
        await WaitUntil(() => !dialog.IsReading);

        dialog.ImportRoutes = false;
        Assert.True(dialog.CanContinue);
        Assert.Equal(new MapImportOptions(true, true, false), dialog.ToPlan().Options);

        dialog.ImportBusStops = false;
        dialog.ImportDestinations = false;
        Assert.False(dialog.CanContinue);
    }

    [Fact]
    public async Task Choosing_another_map_while_one_is_being_read_keeps_only_the_last_answer()
    {
        var slow = new TaskCompletionSource();
        var dialog = new NewFileViewModel([new OmsiMap("First", "first"), new OmsiMap("Second", "second")], folder =>
        {
            if (folder == "first") slow.Task.Wait();       // the first map takes a while
            return Sample(folder);
        });
        dialog.FromMap = true;

        dialog.SelectedMap = dialog.Maps[0];
        dialog.SelectedMap = dialog.Maps[1];
        await WaitUntil(() => dialog.Data is not null);
        slow.SetResult();                                    // the first answer arrives late
        await Task.Delay(200);

        Assert.Equal("second", dialog.Data!.Name);
        Assert.False(dialog.IsReading);
    }

    [Fact]
    public void A_map_folder_picked_by_hand_is_added_once_and_selected()
    {
        var dialog = new NewFileViewModel([], _ => Sample("x"));
        Assert.False(dialog.HasMaps);
        dialog.FromMap = true;
        Assert.Contains("No maps were found", dialog.Summary);

        dialog.AddMapFolder(Path.Combine(_dir, "My Map"));
        dialog.AddMapFolder(Path.Combine(_dir, "My Map") + Path.DirectorySeparatorChar);

        Assert.Single(dialog.Maps);
        Assert.Equal("My Map", dialog.SelectedMap!.Name);
        Assert.True(dialog.FromMap);
    }

    // ---- making the file ---------------------------------------------------------------------------------

    MainViewModel NewViewModel() => new(AppSettings.Load(Path.Combine(_dir, "settings.json")), Path.Combine(_dir, "presets"));

    [Fact]
    public void A_file_made_from_a_map_opens_with_its_stops_destinations_and_routes_and_says_so()
    {
        var vm = NewViewModel();
        string path = Path.Combine(_dir, "Cotterell.hof");

        Assert.True(vm.CreateNew(path, Sample("Cotterell")));

        Assert.Equal("Cotterell", vm.Name);
        Assert.Equal(2, vm.BusStops.Count);
        Assert.Equal(2, vm.Termini.Count);                       // not in service, and the map's destination
        Assert.Single(vm.Routes);
        Assert.Equal(["One", "Two"], vm.Routes[0].Stops.Select(s => s.Name));
        Assert.Equal("Created Cotterell.hof from the map Cotterell: 1 destinations, 2 bus stops, 1 routes", vm.Status);
        Assert.Empty(vm.Problems);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Parts_left_out_stay_out_when_the_file_is_made()
    {
        var vm = NewViewModel();

        Assert.True(vm.CreateNew(Path.Combine(_dir, "x.hof"), Sample("x"), new MapImportOptions(BusStops: true, Destinations: false, Routes: false)));

        Assert.Equal(2, vm.BusStops.Count);
        Assert.Empty(vm.Routes);
        Assert.Single(vm.Termini);
    }
}
