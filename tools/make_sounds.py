#!/usr/bin/env python3
"""Synthesises AVAFoodFight's sound effects and tunes into ../src/AVAFoodFight/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAFoodFight", "Sounds")
RNG = random.Random(1983)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def drums(bpm, bars):
    """A marching snare: a roll on the first beat of each bar, single taps on the rest, and a bass drum under it."""
    beat = 60.0 / bpm
    out = []
    for _ in range(bars):
        for b in range(4):
            if b == 0:
                hit = cat(*[tone(beat / 4, 1800, 900, "noise", attack=0.001, release=1.5, gain=0.5) for _ in range(4)])
            else:
                hit = cat(tone(beat / 2, 2000, 700, "noise", attack=0.001, release=2.5, gain=0.6), silence(beat / 2))
            kick = cat(tone(beat / 2, 110, 45, "sine", attack=0.002, release=1.5, gain=0.9), silence(beat / 2)) if b % 2 == 0 else silence(beat)
            out.extend(mix(hit, kick)[: int(RATE * beat)])
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # Grabbing a handful: a quick rising blip. Throwing it: a whoosh.
    write("Pickup.wav", cat(tone(0.05, hz("E5"), kind="pulse", release=0.3), tone(0.09, hz("B5"), kind="pulse", release=0.4)), 0.28)
    write("Throw.wav", mix(tone(0.18, 400, 1600, "noise", attack=0.03, release=0.7), tone(0.14, 300, 700, "tri", release=0.8, gain=0.3)), 0.2)
    # A splat: a wet slap with a sliding thud under it.
    write("Splat.wav", mix(tone(0.22, 2400, 200, "noise", attack=0.001, release=0.5), tone(0.2, 220, 60, "square", release=0.6, gain=0.5)), 0.4)
    # A cook hit and down his manhole: a slide whistle down and a clang. Coming up again: a slide up.
    write("CookDown.wav", cat(mix(tone(0.12, 2600, 300, "noise", release=0.5, gain=0.7), tone(0.12, 200, 70, "square", release=0.6, gain=0.5)),
                              tone(0.4, 1400, 300, "sine", vibrato=0.01, release=0.6), mix(tone(0.25, 620, kind="square", release=0.2, gain=0.3), tone(0.25, 930, kind="square", release=0.2, gain=0.2))), 0.32)
    write("CookUp.wav", cat(mix(tone(0.12, 520, kind="square", release=0.3, gain=0.4), tone(0.12, 780, kind="square", release=0.3, gain=0.3)), tone(0.3, 300, 1100, "sine", vibrato=0.01, release=0.5)), 0.22)
    # Caught: a bonk. Down a manhole: a long fall and a far-off clang.
    write("Hit.wav", cat(tone(0.08, 300, 120, "square", release=0.5), tone(0.3, 600, 200, "tri", vibrato=0.04, vib_rate=14, release=0.5)), 0.4)
    write("Fall.wav", cat(tone(0.9, 1500, 120, "pulse", release=0.3, vibrato=0.01), mix(tone(0.4, 400, kind="square", release=0.15, gain=0.25), tone(0.4, 560, kind="square", release=0.15, gain=0.2))), 0.32)
    # The ice cream gone: a sad wah-wah.
    write("Melted.wav", cat(*[tone(d, hz(n), hz(n) * 0.94, kind="saw", vibrato=0.02, vib_rate=6, release=0.4) for n, d in [("E4", 0.3), ("D#4", 0.3), ("D4", 0.3), ("C#4", 0.9)]]), 0.38)
    # Got there: a slurp and a happy run.
    write("Yum.wav", cat(tone(0.25, 300, 900, "tri", vibrato=0.06, vib_rate=20, release=0.5), *[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["C6", "E6", "G6"]]), 0.34)
    # The last seconds ticking away.
    write("Tick.wav", tone(0.05, 1800, 1500, "square", attack=0.001, release=0.6), 0.2)
    write("LevelStart.wav", cat(*[tone(d, hz(n), kind="pulse", release=0.3) for n, d in [("G5", 0.08), ("G5", 0.08), ("C6", 0.08), ("E6", 0.24)]]), 0.3)
    write("LevelClear.wav", tune(160, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("A5", 0.5), ("C6", 0.5), ("G5", 1)], [("C3", 1), ("F2", 1), ("G2", 1), ("C3", 1)], kind="pulse"), 0.4)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["C6", "G6", "C6", "G6", "C7"]]), 0.36)

    # The run for the ice cream: a bouncy oom-pah over a bass (an original composition), a whole number of
    # beats long so it loops cleanly.
    lead = [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("E5", 0.5), ("A5", 1), ("G5", 1),
            ("F5", 0.5), ("D5", 0.5), ("B4", 0.5), ("D5", 0.5), ("G5", 1), ("F5", 1),
            ("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("G5", 0.5), ("A5", 0.5), ("G5", 0.5), ("E5", 0.5), ("C5", 0.5),
            ("D5", 0.5), ("E5", 0.5), ("F5", 0.5), ("D5", 0.5), ("C5", 2)]
    bass = [("C3", 0.5), ("G3", 0.5)] * 4 + [("G2", 0.5), ("D3", 0.5)] * 4 + [("C3", 0.5), ("G3", 0.5)] * 2 + [("A2", 0.5), ("E3", 0.5)] * 2 \
        + [("G2", 0.5), ("D3", 0.5)] * 2 + [("C3", 1), ("G2", 1)]
    melody = tune(132, lead, bass, kind="pulse")
    write("Music.wav", mix([v * 0.6 for v in melody], drums(132, 4)), 0.28)

    # The title: the same tune with a second strain.
    title_lead = lead + [("A5", 0.5), ("G5", 0.5), ("F5", 0.5), ("A5", 0.5), ("G5", 1), ("E5", 1),
                         ("F5", 0.5), ("E5", 0.5), ("D5", 0.5), ("F5", 0.5), ("E5", 1), ("C5", 1),
                         ("D5", 0.5), ("E5", 0.5), ("F5", 0.5), ("G5", 0.5), ("A5", 0.5), ("B5", 0.5), ("C6", 1),
                         ("G5", 0.5), ("E5", 0.5), ("D5", 0.5), ("B4", 0.5), ("C5", 2)]
    title_bass = bass + [("F2", 0.5), ("C3", 0.5)] * 4 + [("C3", 0.5), ("G3", 0.5)] * 4 + [("G2", 0.5), ("D3", 0.5)] * 4 \
        + [("G2", 0.5), ("D3", 0.5)] * 2 + [("C3", 1), ("G2", 1)]
    write("TitleTune.wav", cat(mix(tune(138, title_lead, title_bass, kind="pulse"), drums(138, 8)), silence(1.0)), 0.4)
    write("StartTune.wav", tune(160, [("C5", 0.5), ("D5", 0.5), ("E5", 0.5), ("G5", 1), ("E5", 0.5), ("C6", 1)], [("C3", 1), ("G2", 1), ("C3", 2)], kind="pulse"), 0.42)
    write("GameOver.wav", tune(70, [("G4", 1), ("F#4", 1), ("F4", 1), ("E4", 1), ("C4", 4)], [("C3", 2), ("B2", 2), ("A#2", 2), ("C2", 2)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
