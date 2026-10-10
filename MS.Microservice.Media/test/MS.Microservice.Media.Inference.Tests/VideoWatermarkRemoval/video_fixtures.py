"""Small deterministic local video fixtures; no downloaded assets or model execution."""
import argparse
import subprocess
from fractions import Fraction
from pathlib import Path
import av
import numpy as np
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser()
parser.add_argument("directory")
parser.add_argument("--extended", action="store_true")
parser.add_argument("--automatic-fixtures", type=Path)
args = parser.parse_args()
directory = Path(args.directory)
directory.mkdir(exist_ok=True)
height, width = 96, 128
yy, xx = np.mgrid[:height, :width]
base = np.stack([(xx * 3 + yy * 2) % 256, (xx * 7 + yy) % 256, (xx + yy * 5) % 256], axis=2).astype(np.uint8)
mask = np.zeros((height, width), np.uint8)
mask[34:51, 8:66] = 255
Image.fromarray(mask).save(directory / "mask.png")
Image.fromarray(np.zeros((height, width), np.uint8)).save(directory / "empty-mask.png")
Image.fromarray(np.full((height, width), 255, np.uint8)).save(directory / "full-mask.png")
Image.fromarray(np.zeros((3, 4), np.uint8)).save(directory / "wrong-size.png")
invalid = mask.copy()
invalid[0, 0] = 1
Image.fromarray(invalid).save(directory / "nonbinary.png")
Image.fromarray(np.zeros((1, 1), np.uint8)).save(directory / "tiny-mask.png")
for name, shape, pts, durations, audio in [
    ("source.mkv", (height, width), [0, 40, 140, 300, 410], [40, 100, 160, 110, 80], True),
    ("tiny.mkv", (1, 1), [0], [40], False),
    ("fine.mov", (height, width), [0, 3003, 9009, 18018], [3003, 6006, 9009, 3003], False),
    ("collision.mov", (height, width), [0, 20, 40, 60], [20, 20, 20, 20], False),
]:
    fine = name.endswith("mov")
    tb = Fraction(1, 90000) if fine else Fraction(1, 1000)
    with av.open(str(directory / name), "w", format="mov" if fine else "matroska") as output:
        # Audio first intentionally defeats assumptions about globally contiguous video/audio indices.
        sound = output.add_stream("pcm_u8", rate=44100) if audio else None
        if sound:
            sound.layout = "mono"
        stream = output.add_stream("png" if fine else "ffv1", rate=25)
        stream.height, stream.width = shape
        stream.pix_fmt = "rgb24" if fine else "bgr0"
        stream.time_base = stream.codec_context.time_base = tb
        stream.codec_context.thread_count = 1
        for index, (tick, duration) in enumerate(zip(pts, durations)):
            image = np.roll(base, index * 2, axis=1).copy() if shape != (1, 1) else np.array([[[13, 79, 121]]], np.uint8)
            if shape != (1, 1):
                image[35:49, 10 + index * 5:40 + index * 5] = 245
            frame = av.VideoFrame.from_ndarray(image, format="rgb24")
            frame.pts, frame.time_base, frame.duration = tick, tb, duration
            for packet in stream.encode(frame):
                packet.duration = duration
                output.mux(packet)
        for packet in stream.encode(None):
            output.mux(packet)
        if sound:
            # Unsigned PCM silence at128 and sine ensure float32 normalization subtracts its midpoint.
            count = 26019
            samples = np.rint(128 + np.sin(np.arange(count) * 2 * np.pi * 440 / 44100) * 25).astype(np.uint8)
            samples[:2048] = 128
            for offset in range(0, count, 1024):
                frame = av.AudioFrame.from_ndarray(samples[None, offset:offset + 1024].copy(), format="u8", layout="mono")
                frame.sample_rate, frame.time_base, frame.pts = 44100, Fraction(1, 44100), offset + 2205
                for packet in sound.encode(frame):
                    output.mux(packet)
            for packet in sound.encode(None):
                output.mux(packet)

if args.automatic_fixtures:
    assets = args.automatic_fixtures
    background = np.asarray(Image.open(assets / "negative-scene.png").convert("RGB"))
    fixed = np.asarray(Image.open(assets / "positive-repeated.png").convert("RGB"))
    subtitle = np.asarray(Image.open(assets / "negative-subtitle.png").convert("RGB"))
    font_path = "C:/Windows/Fonts/arial.ttf" if Path("C:/Windows/Fonts/arial.ttf").exists() else "DejaVuSans.ttf"
    font = ImageFont.truetype(font_path, 24)
    points, durations = [0, 40, 140, 300], [40, 100, 160, 80]
    for name in ["moving", "fixed", "subtitle", "scene"]:
        with av.open(str(directory / f"auto-{name}.mkv"), "w", format="matroska") as output:
            stream = output.add_stream("ffv1", rate=25)
            stream.height, stream.width, stream.pix_fmt = 384, 512, "bgr0"
            stream.time_base = stream.codec_context.time_base = Fraction(1, 1000)
            stream.codec_context.thread_count = 1
            sound = output.add_stream("pcm_s16le", rate=48000)
            sound.layout = "mono"
            for index, (tick, duration) in enumerate(zip(points, durations)):
                image = fixed if name == "fixed" else subtitle if name == "subtitle" else background
                if name == "moving":
                    overlay = Image.new("RGBA", (512, 384))
                    ImageDraw.Draw(overlay).text((20 + 40 * index, 155 + 3 * (index % 3)), "SAMPLE OWNER", font=font, fill=(255, 255, 255, 150))
                    image = np.asarray(Image.alpha_composite(Image.fromarray(np.roll(background, 2 * index, axis=1)).convert("RGBA"), overlay).convert("RGB"))
                frame = av.VideoFrame.from_ndarray(image, format="rgb24")
                frame.pts, frame.time_base, frame.duration = tick, Fraction(1, 1000), duration
                for packet in stream.encode(frame):
                    packet.duration = duration
                    output.mux(packet)
            for packet in stream.encode(None):
                output.mux(packet)
            samples = (np.sin(np.arange(24000) * 2 * np.pi * 880 / 48000) * 9000).astype(np.int16)
            for offset in range(0, len(samples), 1024):
                frame = av.AudioFrame.from_ndarray(samples[None, offset:offset + 1024].copy(), format="s16", layout="mono")
                frame.sample_rate, frame.time_base, frame.pts = 48000, Fraction(1, 48000), offset + 1200
                for packet in sound.encode(frame):
                    output.mux(packet)
            for packet in sound.encode(None):
                output.mux(packet)
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", str(directory / "auto-fixed.mkv"), "-c:v", "png", "-threads", "1",
                    "-c:a", "pcm_s16le", "-fps_mode", "passthrough", str(directory / "auto-fixed.mov")], check=True, timeout=20)
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-display_rotation:v:0", "90", "-i", str(directory / "auto-fixed.mov"), "-c", "copy",
                    str(directory / "auto-rotation90.mov")], check=True, timeout=20)

if args.extended:
    def ffmpeg(*arguments):
        subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-nostdin", "-y", *map(str, arguments)], check=True, timeout=20)

    ffmpeg("-copyts", "-i", directory / "source.mkv", "-map", "0:a", "-map", "0:v", "-c:v", "libx264", "-threads", "1", "-pix_fmt", "yuv420p", "-c:a", "aac",
           "-vf", "fps=25,setpts=PTS+0.2/TB", "-af", "asetpts=PTS+0.03/TB", "-fps_mode", "passthrough", "-avoid_negative_ts", "disabled", directory / "aac-origin.mp4")
    ffmpeg("-copyts", "-i", directory / "source.mkv", "-map", "0:a", "-map", "0:v", "-c:v", "libx264", "-threads", "1", "-pix_fmt", "yuv420p", "-c:a", "aac",
           "-vf", "setpts=PTS+0.2/TB", "-af", "asetpts=PTS+0.03/TB", "-fps_mode", "passthrough", "-avoid_negative_ts", "disabled", directory / "unknown-last-duration.mp4")
    tones = "|".join(f"0.1*sin({440 + i * 110}*2*PI*t)" for i in range(6))
    ffmpeg("-copyts", "-i", directory / "source.mkv", "-f", "lavfi", "-i", f"aevalsrc={tones}:s=44100:d=0.8", "-map", "0:a", "-map", "1:a", "-map", "0:v",
           "-c:v", "libx264", "-threads", "1", "-pix_fmt", "yuv420p", "-vf", "fps=25", "-c:a", "aac", "-af:a:1", "asetpts=PTS+0.025/TB", "-fps_mode", "passthrough", "-avoid_negative_ts", "disabled", directory / "aac-surround.mp4")
    for name, flags in [("rotation90", ["-display_rotation:v:0", "90"]), ("rotation37", ["-display_rotation:v:0", "37"]), ("reflection", ["-display_hflip:v:0"])]:
        ffmpeg(*flags, "-i", directory / "aac-origin.mp4", "-c", "copy", directory / f"{name}.mp4")
    ffmpeg("-i", directory / "source.mkv", "-vf", "setsar=2/1", "-c:v", "libx264", "-threads", "1", "-pix_fmt", "yuv420p", "-an", directory / "nonsquare.mp4")
    ffmpeg("-copyts", "-i", directory / "source.mkv", "-map", "0:v", "-map", "0:a", "-c:v", "copy", "-af", "asetpts=PTS+gte(T\\,0.09)*0.1/TB", "-c:a", "pcm_s16le", "-avoid_negative_ts", "disabled", directory / "audio-gap.mkv")
    ffmpeg("-i", directory / "source.mkv", "-f", "lavfi", "-i", f"aevalsrc={tones}:s=44100:d=0.8", "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "pcm_s16le", directory / "unknown-surround.mkv")
    with av.open(str(directory / "negative-origin.mkv"), "w", format="matroska", options={"avoid_negative_ts": "disabled"}) as output:
        stream = output.add_stream("ffv1", rate=25)
        stream.width, stream.height, stream.pix_fmt = width, height, "bgr0"
        stream.time_base = stream.codec_context.time_base = Fraction(1, 1000)
        for tick in [-200, -160, -60, 100, 210]:
            frame = av.VideoFrame.from_ndarray(base, format="rgb24")
            frame.pts, frame.time_base, frame.duration = tick, Fraction(1, 1000), 40
            for packet in stream.encode(frame):
                packet.duration = 40
                output.mux(packet)
        for packet in stream.encode(None):
            output.mux(packet)
