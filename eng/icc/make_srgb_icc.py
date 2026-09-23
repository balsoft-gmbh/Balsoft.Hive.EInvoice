#!/usr/bin/env python3
"""Builds a compact sRGB ICC v2 display profile for the PDF/A output intent.

The profile is generated from the published sRGB definition (IEC 61966-2-1): primaries and
white point adapted to the D50 profile connection space with the Bradford transform, and
the sRGB tone curve sampled at 1024 points. Generating it keeps Hive.EInvoice.Pdf free of
third-party profile files with their own licence terms.

Usage: python eng/icc/make_srgb_icc.py src/Hive.EInvoice.Pdf/Resources/sRGB.icc
"""
import struct
import sys


def s15f16(v: float) -> bytes:
    return struct.pack(">i", int(round(v * 65536)))


def xyz(x: float, y: float, z: float) -> bytes:
    return b"XYZ " + b"\0" * 4 + s15f16(x) + s15f16(y) + s15f16(z)


def srgb_to_linear(c: float) -> float:
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def curve(points: int = 1024) -> bytes:
    values = [int(round(srgb_to_linear(i / (points - 1)) * 65535)) for i in range(points)]
    return b"curv" + b"\0" * 4 + struct.pack(">I", points) + b"".join(struct.pack(">H", v) for v in values)


def text_description(text: str) -> bytes:
    ascii_text = text.encode("ascii") + b"\0"
    return (b"desc" + b"\0" * 4 + struct.pack(">I", len(ascii_text)) + ascii_text
            + struct.pack(">I", 0) + struct.pack(">I", 0)          # no Unicode description
            + struct.pack(">H", 0) + struct.pack(">B", 0) + b"\0" * 67)  # no ScriptCode description


def text(value: str) -> bytes:
    return b"text" + b"\0" * 4 + value.encode("ascii") + b"\0"


def build() -> bytes:
    # sRGB primaries (IEC 61966-2-1) adapted from D65 to D50 with Bradford, as used by the
    # ICC's own sRGB v2 profiles.
    tags = [
        (b"desc", text_description("sRGB IEC61966-2.1")),
        (b"cprt", text("No copyright, use freely")),
        (b"wtpt", xyz(0.9642, 1.0, 0.8249)),
        (b"rXYZ", xyz(0.4360747, 0.2225045, 0.0139322)),
        (b"gXYZ", xyz(0.3850649, 0.7168786, 0.0971045)),
        (b"bXYZ", xyz(0.1430804, 0.0606169, 0.7141733)),
        (b"rTRC", curve()),
    ]
    # gTRC and bTRC share the rTRC data (allowed by ICC.1: tags may point at the same data).
    header_size, tag_count = 128, len(tags) + 2
    offset = header_size + 4 + 12 * tag_count
    table, data, positions = [], b"", {}
    for sig, body in tags:
        while (offset + len(data)) % 4:
            data += b"\0"
        positions[sig] = (offset + len(data), len(body))
        table.append((sig, *positions[sig]))
        data += body
    table.append((b"gTRC", *positions[b"rTRC"]))
    table.append((b"bTRC", *positions[b"rTRC"]))
    while (offset + len(data)) % 4:
        data += b"\0"

    body = struct.pack(">I", tag_count) + b"".join(sig + struct.pack(">II", o, n) for sig, o, n in table) + data
    size = header_size + len(body)
    header = (struct.pack(">I", size) + b"\0" * 4                      # size, preferred CMM
              + bytes([2, 0x10, 0, 0])                                  # version 2.1.0
              + b"mntr" + b"RGB " + b"XYZ "                             # class, colour space, PCS
              + struct.pack(">6H", 2026, 9, 23, 0, 0, 0)                 # creation date
              + b"acsp" + b"\0" * 4                                     # signature, platform
              + b"\0" * 4 + b"\0" * 4 + b"\0" * 4 + b"\0" * 8           # flags, manufacturer, model, attributes
              + struct.pack(">I", 0)                                    # rendering intent: perceptual
              + s15f16(0.9642) + s15f16(1.0) + s15f16(0.8249)           # PCS illuminant D50
              + b"\0" * 4                                               # creator
              + b"\0" * 44)                                             # reserved (v2)
    assert len(header) == header_size
    return header + body


if __name__ == "__main__":
    out = build()
    with open(sys.argv[1], "wb") as f:
        f.write(out)
    print(f"{sys.argv[1]}: {len(out)} bytes")
