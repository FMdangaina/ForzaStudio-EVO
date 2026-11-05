"""Anark .bgf reader for research: graph + the five tail blocks, every reference named.

Usage:
  python anark_tail.py check <folder> [<folder>...]   parse every .bgf below the folders, report grammar coverage
  python anark_tail.py dump <file.bgf> [eventnames.txt] print the tail blocks with names

Verified 2026-11-05 on the 141 FM4 UI scenes (Media/UI.zip, scenes/ui4) and the 233 FM4 car dashes: 374/374.
See docs/ANARK_DASH.md, section "Tail blocks, decoded".
"""
import struct, sys, os, glob, collections

def H(s):
    """sdbm, full 32 bits. Elements: H(name). Contract behaviours: H(name + '_CONTRACT').
    Events, commands and slide names in the event block: H(name) & 0x7FFFFFFF. Properties: low 27 bits + kind in the top 5."""
    h = 0
    for c in s:
        h = ((h + ord(c)) * 65599) & 0xFFFFFFFF
    return h

COMMANDS = {H(n) & 0x7FFFFFFF: n for n in ['COMMAND_GOTOSLIDENAME', 'COMMAND_SETPROPERTY', 'COMMAND_FIREEVENT', 'COMMAND_PLAY',
                                           'COMMAND_GOTOTIME', 'COMMAND_PAUSE', 'COMMAND_GOTOSLIDE', 'COMMAND_SETATTRIBUTE']}
PROPS = ['position.x', 'position.y', 'position.z', 'rotation.x', 'rotation.y', 'rotation.z', 'scale.x', 'scale.y', 'scale.z', 'pivot.x', 'pivot.y',
         'pivot.z', 'opacity', 'textstring', 'diffuse.r', 'diffuse.g', 'diffuse.b', 'textcolor.r', 'textcolor.g', 'textcolor.b', 'eyeball',
         'starttime', 'endtime', 'timeoffset', 'size', 'font', 'positionu', 'positionv', 'scaleu', 'scalev', 'rotationuv']
PROPNAMES = {H(p) & 0x7FFFFFF: p for p in PROPS}

def u32(d, o):
    return struct.unpack_from('>I', d, o)[0]

def words(b):
    return [u32(b, i) for i in range(0, len(b) - 3, 4)]

class Bgf:
    def __init__(self, d):
        assert d[6:14] == b'AnarkBGF', 'not an Anark .bgf'
        self.hdr = d[:0x88]
        o = 0x88
        end = u32(d, o) + o + 4; o += 4
        self.strings = []
        while o < end:
            n = u32(d, o); o += 4
            self.strings.append(d[o:o + n].decode('latin1')); o += n
        ge = u32(d, o) + o + 4; o += 4
        self.el = []
        while o < ge:
            h, par, idn = struct.unpack_from('>IiI', d, o); o += 12
            t, fl = d[o], d[o + 1]; o += 2
            pre = None
            if t == 7:
                pre = d[o:o + 5]; o += 5
            n = u32(d, o); o += 4
            props = [struct.unpack_from('>II', d, o + 8 * k) for k in range(n)]
            o += 8 * n
            self.el.append(dict(h=h, par=par, id=idn, t=t, fl=fl, pre=pre, props=props))
        if o != ge:
            raise ValueError('graph overrun')
        self.blocks = []
        for i in range(5):
            n = u32(d, o); o += 4
            self.blocks.append(d[o:o + n]); o += n
        if o != len(d):
            raise ValueError('%d bytes after the five tail blocks' % (len(d) - o))
        self.declared = self.parse_declared(words(self.blocks[0]))
        self.tracks = self.parse_tracks(words(self.blocks[1]))
        self.events = self.parse_events(words(self.blocks[2]))

    def parse_declared(self, w):
        """Block 1: per behaviour (u32 element, u32 n, n x u32 string index) = the event names it declares."""
        r, i = [], 0
        while i < len(w):
            el, n = w[i], w[i + 1]
            sl = w[i + 2:i + 2 + n]
            if el >= len(self.el) or self.el[el]['t'] != 8 or any(s >= len(self.strings) for s in sl):
                raise ValueError('block 1: bad entry at word %d' % i)
            r.append((el, sl)); i += 2 + n
        return r

    def parse_tracks(self, w):
        """Block 2: (u32 element, u32 property hash, u32 type<<16 | keys, keys x 6 words)."""
        r, i = [], 0
        while i < len(w):
            el, ph, tk = w[i], w[i + 1], w[i + 2]
            if el >= len(self.el):
                raise ValueError('block 2: bad element at word %d' % i)
            nk = tk & 0xFFFF
            r.append((el, ph, tk >> 16, [w[i + 3 + 6 * k:i + 9 + 6 * k] for k in range(nk)]))
            i += 3 + 6 * nk
        if i != len(w):
            raise ValueError('block 2 overrun')
        return r

    def parse_events(self, w):
        """Block 3: groups (u32 owner element, u32 n events), event (u32 event hash, u32 n actions),
        action (u32 target element, u32 target's component, u32 command hash, u32 arg1, u32 arg2)."""
        r, i = [], 0
        while i < len(w):
            owner, ne = w[i], w[i + 1]; i += 2
            if owner >= len(self.el):
                raise ValueError('block 3: bad owner at word %d' % (i - 2))
            evs = []
            for _ in range(ne):
                eh, na = w[i], w[i + 1]; i += 2
                acts = [tuple(w[i + 5 * k:i + 5 * k + 5]) for k in range(na)]
                for a in acts:
                    if a[0] >= len(self.el) or a[1] >= len(self.el) or a[2] not in COMMANDS:
                        raise ValueError('block 3: bad action at word %d' % i)
                i += 5 * na
                evs.append((eh, acts))
            r.append((owner, evs))
        if i != len(w):
            raise ValueError('block 3 overrun')
        return r

    def names(self, extra=()):
        n = {}
        for s in list(self.strings) + list(extra):
            n.setdefault(H(s), s)
            n.setdefault(H(s) & 0x7FFFFFFF, s)
            n.setdefault(H(s + '_CONTRACT'), s + '_CONTRACT')
        return n

def fmt_el(b, i, n):
    e = b.el[i]
    return '#%d %s' % (i, n.get(e['h'], '%08X' % e['h']))

def dump(path, eventnames=None):
    b = Bgf(open(path, 'rb').read())
    extra = [l.strip() for l in open(eventnames)] if eventnames else []
    n = b.names(extra)
    print('elements %d, strings %d, tail block sizes %s' % (len(b.el), len(b.strings), [len(x) for x in b.blocks]))
    print('== block 1: events declared by behaviours')
    for el, sl in b.declared:
        print('  %s: %s' % (fmt_el(b, el, n), ', '.join(b.strings[s] for s in sl)))
    print('== block 2: animation tracks (%d)' % len(b.tracks))
    for el, ph, ty, keys in b.tracks[:50]:
        print('  %s %s type %d keys %d' % (fmt_el(b, el, n), PROPNAMES.get(ph & 0x7FFFFFF, '%08X' % ph), ty, len(keys)))
    print('== block 3: events')
    for owner, evs in b.events:
        print('  owner %s' % fmt_el(b, owner, n))
        for eh, acts in evs:
            print('    on %s' % n.get(eh, '%08X' % eh))
            for t, c, cmd, a1, a2 in acts:
                cn = COMMANDS[cmd]
                if cn == 'COMMAND_SETPROPERTY':
                    arg = '%s = %g' % (PROPNAMES.get(a1 & 0x7FFFFFF, '%08X' % a1), struct.unpack('>f', struct.pack('>I', a2))[0])
                elif cn in ('COMMAND_GOTOSLIDENAME', 'COMMAND_FIREEVENT'):
                    arg = n.get(a1, '%08X' % a1) + ('' if a2 == 0 else ' %08X' % a2)
                else:
                    arg = '%d %d' % (a1, a2)
                print('      %s -> %s (ctx %s) %s' % (cn[8:], fmt_el(b, t, n), fmt_el(b, c, n), arg))

def check(roots):
    st = collections.Counter()
    for root in roots:
        for f in sorted(glob.glob(os.path.join(root, '**', '*.bgf'), recursive=True)):
            try:
                b = Bgf(open(f, 'rb').read())
            except Exception as e:
                st['bad'] += 1
                print('BAD', f, e)
                continue
            st['ok'] += 1
            st['events'] += sum(len(e) for o, e in b.events)
            st['actions'] += sum(len(a) for o, e in b.events for h, a in e)
            st['tracks'] += len(b.tracks)
            decl = {el: {H(b.strings[s]) & 0x7FFFFFFF for s in sl} for el, sl in b.declared}
            for owner, evs in b.events:
                for eh, acts in evs:
                    st['event declared by owner' if eh in decl.get(owner, ()) else 'event not declared by owner'] += 1
    for k, v in sorted(st.items()):
        print('%-30s %d' % (k, v))

if __name__ == '__main__':
    if len(sys.argv) >= 3 and sys.argv[1] == 'check':
        check(sys.argv[2:])
    elif len(sys.argv) >= 3 and sys.argv[1] == 'dump':
        dump(sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else None)
    else:
        print(__doc__)
