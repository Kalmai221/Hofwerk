"""A small Markdown to HTML converter for the documents this project writes (changelog, roadmap, README sections, preset guide).

It covers what those files use: headings, paragraphs, bold, italic, code, links, images, tables, nested lists (with [ ] and [x]),
fenced code, quotes and rules. Nothing else is promised, and nothing needs installing.
"""
import html
import re

HEADING = re.compile(r'^(#{1,6})\s+(.*?)\s*#*\s*$')
FENCE = re.compile(r'^(\s*)```(\w*)\s*$')
LIST_ITEM = re.compile(r'^(\s*)([-*+]|\d+[.)])\s+(.*)$')
TABLE_SEPARATOR = re.compile(r'^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$')
REFERENCE = re.compile(r'^\[[^\]]+\]:\s+\S+')
RULE = re.compile(r'^\s*([-*_])\s*(\1\s*){2,}$')


def slug(text):
    text = re.sub(r'<[^>]+>', '', text)
    text = re.sub(r'[^\w\s-]', '', text.lower())
    return re.sub(r'[\s_]+', '-', text).strip('-')


class Converter:
    def __init__(self, links=None, images=None):
        # links: repository file -> page on the site; images: repository image -> path on the site
        self.links = links or {}
        self.images = images or {}

    # ---------------------------------------------------------------- inline

    def inline(self, text):
        spans = []

        def stash(match):
            spans.append('<code>' + html.escape(match.group(1)) + '</code>')
            return '\x00%d\x00' % (len(spans) - 1)

        text = re.sub(r'`([^`]+)`', stash, text)
        text = html.escape(text, quote=False)
        text = re.sub(r'!\[([^\]]*)\]\(([^)\s]+)\)', self._image, text)
        text = re.sub(r'\[([^\]]+)\]\(([^)\s]+)\)', self._link, text)
        text = re.sub(r'\*\*(.+?)\*\*', r'<strong>\1</strong>', text)
        text = re.sub(r'(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])', r'<em>\1</em>', text)
        text = re.sub(r'(?<!\w)_(?!\s)(.+?)(?<!\s)_(?!\w)', r'<em>\1</em>', text)
        text = text.replace('&lt;br&gt;', '<br>')
        return re.sub(r'\x00(\d+)\x00', lambda m: spans[int(m.group(1))], text)

    def _image(self, match):
        alt, target = match.group(1), html.unescape(match.group(2))
        src = self.images.get(target)
        if src is None:
            return alt
        return '<img src="%s" alt="%s" loading="lazy">' % (src, alt)

    def _link(self, match):
        label, target = match.group(1), html.unescape(match.group(2))
        if re.match(r'^(https?://|mailto:)', target):
            return '<a href="%s">%s</a>' % (html.escape(target), label)
        if target.startswith('#'):
            return '<a href="%s">%s</a>' % (html.escape(target), label)
        path, _, fragment = target.partition('#')
        page = self.links.get(path)
        if page is None:
            return label    # a link to a file that is not on the site: keep the words, drop the link
        return '<a href="%s">%s</a>' % (page + ('#' + fragment if fragment else ''), label)

    # ----------------------------------------------------------------- blocks

    def convert(self, markdown):
        lines = markdown.replace('\r\n', '\n').replace('\r', '\n').split('\n')
        out = []
        i = 0
        while i < len(lines):
            line = lines[i]
            if not line.strip():
                i += 1
            elif line.strip().startswith('<!--'):
                while i < len(lines) and '-->' not in lines[i]:
                    i += 1
                i += 1
            elif REFERENCE.match(line):
                i += 1
            elif FENCE.match(line):
                i = self._fence(lines, i, out)
            elif HEADING.match(line):
                level, text = HEADING.match(line).groups()
                text = re.sub(r'\[([^\]]+)\](?!\()', r'\1', text)          # [1-dev3] -> 1-dev3
                out.append('<h%d id="%s">%s</h%d>' % (len(level), slug(text), self.inline(text), len(level)))
                i += 1
            elif RULE.match(line):
                out.append('<hr>')
                i += 1
            elif line.lstrip().startswith('|') and i + 1 < len(lines) and TABLE_SEPARATOR.match(lines[i + 1]):
                i = self._table(lines, i, out)
            elif LIST_ITEM.match(line):
                i = self._list(lines, i, out)
            elif line.startswith('>'):
                quote = []
                while i < len(lines) and lines[i].startswith('>'):
                    quote.append(lines[i][1:].lstrip())
                    i += 1
                out.append('<blockquote>' + self.convert('\n'.join(quote)) + '</blockquote>')
            else:
                i = self._paragraph(lines, i, out)
        return '\n'.join(out)

    def _fence(self, lines, i, out):
        indent = len(FENCE.match(lines[i]).group(1))
        language = FENCE.match(lines[i]).group(2)
        i += 1
        code = []
        while i < len(lines) and not FENCE.match(lines[i]):
            code.append(lines[i][indent:] if lines[i][:indent].strip() == '' else lines[i])
            i += 1
        out.append('<pre><code%s>%s</code></pre>' % (' class="language-%s"' % language if language else '', html.escape('\n'.join(code))))
        return i + 1

    def _paragraph(self, lines, i, out):
        text = []
        while i < len(lines) and lines[i].strip():
            line = lines[i]
            if text and (HEADING.match(line) or FENCE.match(line) or LIST_ITEM.match(line) or line.startswith('>')
                         or (line.lstrip().startswith('|') and i + 1 < len(lines) and TABLE_SEPARATOR.match(lines[i + 1]))):
                break
            text.append(line.strip())
            i += 1
        if text:
            out.append('<p>' + self.inline(' '.join(text)) + '</p>')
        return i

    def _table(self, lines, i, out):
        def cells(row):
            row = row.strip()
            if row.startswith('|'):
                row = row[1:]
            if row.endswith('|'):
                row = row[:-1]
            return [c.strip().replace('\\|', '|') for c in re.split(r'(?<!\\)\|', row)]

        header = cells(lines[i])
        i += 2
        rows = []
        while i < len(lines) and lines[i].strip().startswith('|'):
            rows.append(cells(lines[i]))
            i += 1
        table = ['<div class="table"><table><thead><tr>' + ''.join('<th>%s</th>' % self.inline(c) for c in header) + '</tr></thead><tbody>']
        for row in rows:
            table.append('<tr>' + ''.join('<td>%s</td>' % self.inline(c) for c in row) + '</tr>')
        table.append('</tbody></table></div>')
        out.append(''.join(table))
        return i

    def _list(self, lines, i, out):
        items = []   # each: dict(indent, ordered, text, code)
        while i < len(lines):
            line = lines[i]
            match = LIST_ITEM.match(line)
            if match and not RULE.match(line):
                indent = len(match.group(1).expandtabs(4))
                items.append({'indent': indent, 'ordered': match.group(2)[0].isdigit(), 'text': match.group(3), 'code': []})
                i += 1
            elif items and line.strip() == '':
                # a blank line belongs to the list only if more of it follows
                j = i
                while j < len(lines) and not lines[j].strip():
                    j += 1
                if j < len(lines) and (LIST_ITEM.match(lines[j]) or (lines[j].startswith(' ') and not HEADING.match(lines[j].strip()))):
                    i = j
                else:
                    break
            elif items and FENCE.match(line) and line.startswith(' '):
                indent = len(FENCE.match(line).group(1))
                i += 1
                while i < len(lines) and not FENCE.match(lines[i]):
                    items[-1]['code'].append(lines[i][indent:])
                    i += 1
                i += 1
            elif items and line.startswith(' '):
                items[-1]['text'] += ' ' + line.strip()
                i += 1
            else:
                break
        out.append(self._render_items(items, 0, items[0]['indent'])[0])
        return i

    def _render_items(self, items, index, base):
        tag = 'ol' if items[index]['ordered'] else 'ul'
        parts = ['<%s>' % tag]
        while index < len(items) and items[index]['indent'] >= base:
            item = items[index]
            if item['indent'] > base:      # deeper than the list we are in but not under an item: treat as the same level
                base = item['indent']
            index += 1
            body = self._item_text(item['text'])
            if item['code']:
                body += '<pre><code>%s</code></pre>' % html.escape('\n'.join(item['code']).rstrip())
            if index < len(items) and items[index]['indent'] > item['indent']:
                nested, index = self._render_items(items, index, items[index]['indent'])
                body += nested
            parts.append('<li>%s</li>' % body)
            if index < len(items) and items[index]['indent'] < base:
                break
        parts.append('</%s>' % tag)
        return ''.join(parts), index

    def _item_text(self, text):
        match = re.match(r'^\[([ xX])\]\s+(.*)$', text)
        if match:
            done = match.group(1) != ' '
            return '<span class="check%s" aria-label="%s">%s</span> %s' % (
                ' done' if done else '', 'done' if done else 'not done yet', '&#10003;' if done else '', self.inline(match.group(2)))
        return self.inline(text)


def section(markdown, heading, level=2):
    """The text under a heading (without the heading itself), up to the next heading of the same or a higher level."""
    marker = '#' * level + ' ' + heading
    lines = markdown.replace('\r\n', '\n').split('\n')
    start = next((i for i, l in enumerate(lines) if l.strip() == marker), None)
    if start is None:
        return ''
    end = len(lines)
    for j in range(start + 1, len(lines)):
        match = HEADING.match(lines[j])
        if match and len(match.group(1)) <= level:
            end = j
            break
    return '\n'.join(lines[start + 1:end]).strip()
