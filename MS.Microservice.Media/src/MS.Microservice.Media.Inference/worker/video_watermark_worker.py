"""Bounded local video repair and validation. No downloads, servers or paid services."""
import argparse
import hashlib
import importlib.metadata
import json
import sys
from contextlib import ExitStack
from fractions import Fraction
from pathlib import Path

import av
import numpy as np
from PIL import Image, ImageFilter

MILLISECOND = Fraction(1, 1000)
IDENTITY = (65536, 0, 0, 0, 65536, 0, 0, 0, 1073741824)
ROTATIONS = {IDENTITY: 0, (0, -65536, 0, 65536, 0, 0, 0, 0, 1073741824): 1,
             (-65536, 0, 0, 0, -65536, 0, 0, 0, 1073741824): 2,
             (0, 65536, 0, -65536, 0, 0, 0, 0, 1073741824): 3}


def milliseconds(value):
    if value < 0:
        raise ValueError("Negative presentation timestamps are unsupported")
    return (value.numerator * 2000 + value.denominator) // (value.denominator * 2)


def video_stream(source):
    selected = next((s for s in source.streams.video if not s.disposition & av.stream.Disposition.attached_pic), None)
    if selected is None:
        raise ValueError("No non-cover-art video stream")
    if selected.sample_aspect_ratio not in [None, Fraction(1, 1)]:
        raise ValueError("Only square-pixel video is supported")
    if selected.codec_context.color_trc in [16, 18] or selected.codec_context.color_primaries == 9:
        raise ValueError("HDR / BT.2020 video is unsupported by the RGB8 repair profile")
    selected.codec_context.thread_count = 1
    return selected


def normalized(frame):
    matrices = [tuple(np.frombuffer(side, dtype=np.int32)) for side in frame.side_data if side.type.name == "DISPLAYMATRIX"]
    if len(matrices) > 1:
        raise ValueError("Ambiguous display matrices")
    matrix = matrices[0] if matrices else IDENTITY
    if matrix not in ROTATIONS:
        raise ValueError("Only identity and exact quarter-turn display matrices are supported; reflections, scale and non-right-angle transforms are rejected")
    if frame.interlaced_frame or any(component.bits != 8 or component.is_alpha for component in frame.format.components):
        raise ValueError("Only progressive 8-bit video without alpha is supported")
    return np.rot90(frame.to_ndarray(format="rgb24"), ROTATIONS[matrix]).copy(), matrix


def audio_info(path, limits, video_first, allow_unlabelled_multichannel=False):
    tracks, total_values = [], 0
    with av.open(str(path)) as container:
        selected_streams = list(container.streams.audio)
        if len(selected_streams) > limits["maximumAudioTracks"]:
            raise ValueError("Too many audio tracks")
        indices = [s.index for s in selected_streams]
    for index in indices:
        with av.open(str(path)) as container:
            selected = container.streams[index]
            digest, count, first, rate, layout = hashlib.sha256(), 0, None, None, None
            maximum_error = Fraction(0)
            for frame in container.decode(selected):
                observed_layout = frame.layout.name
                channels = len(frame.layout.channels)
                unknown_roles = all(channel.name == "NONE" for channel in frame.layout.channels)
                if (observed_layout not in ["mono", "stereo", "2.1", "3.0", "quad", "5.1", "5.1(side)", "7.1"]
                        and not (unknown_roles and (channels <= 2 or allow_unlabelled_multichannel))):
                    raise ValueError("Unsupported or unknown audio channel layout")
                if frame.pts is None or frame.time_base is None:
                    raise ValueError("Audio presentation timestamps are required")
                actual = frame.pts * frame.time_base
                if first is None:
                    first, rate, layout = actual, frame.sample_rate, observed_layout
                    if first < 0 or rate <= 0:
                        raise ValueError("Negative audio timestamps or invalid sample rate are unsupported")
                if rate != frame.sample_rate or layout != observed_layout:
                    raise ValueError("Audio sample rate and layout must remain constant")
                error = abs(actual - first - Fraction(count, rate))
                maximum_error = max(error, maximum_error)
                if error > max(MILLISECOND, frame.time_base, Fraction(1, rate)):
                    raise ValueError("Audio timestamp gaps, overlaps or discontinuities are unsupported")
                array = frame.to_ndarray()
                array = array.T if frame.format.is_planar else array.reshape(frame.samples, channels)
                if np.issubdtype(array.dtype, np.unsignedinteger):
                    half = 2 ** (8 * frame.format.bytes - 1)
                    array = (array.astype(np.float32) - half) / half
                elif np.issubdtype(array.dtype, np.signedinteger):
                    array = array.astype(np.float32) / (2 ** (8 * frame.format.bytes - 1))
                else:
                    array = array.astype(np.float32)
                if not np.isfinite(array).all():
                    raise ValueError("Non-finite audio samples are unsupported")
                digest.update(np.ascontiguousarray(array, dtype=np.float32).tobytes())
                count += frame.samples
                total_values += frame.samples * channels
                if total_values > limits["maximumAudioSampleValues"] or float(actual + Fraction(frame.samples, rate) - min(video_first, first)) > limits["maximumDurationSeconds"]:
                    raise ValueError("Audio sample or presentation duration budget exceeded")
            if first is None:
                raise ValueError("Empty audio tracks are unsupported")
            tracks.append({"streamIndex": index, "sourceCodec": selected.codec_context.name, "sampleRate": rate, "layout": layout,
                           "channels": channels, "samples": count, "firstSeconds": float(first), "firstMilliseconds": milliseconds(first),
                           "channelRoles": [channel.name for channel in frame.layout.channels],
                           "float32Sha256": digest.hexdigest(), "maximumTimelineErrorSeconds": float(maximum_error)})
    return tracks


def masks(request, shape):
    height, width = shape
    if request["staticMaskPath"] is not None:
        with Image.open(request["staticMaskPath"]) as source:
            if source.format != "PNG" or source.size != (width, height) or getattr(source, "n_frames", 1) != 1:
                raise ValueError("The static mask must be a PNG with exactly the display-normalized video dimensions")
            array = np.asarray(source.convert("RGBA"))
        if not ((array[:, :, 3] == 255).all() and (array[:, :, 0] == array[:, :, 1]).all() and (array[:, :, 0] == array[:, :, 2]).all()
                and np.isin(array[:, :, 0], [0, 255]).all()):
            raise ValueError("The static mask must be opaque binary grayscale, white=edit and black=keep")
        selected = array[:, :, 0] == 255
        return lambda seconds: selected
    keyframes = request["rectangleKeyframes"]
    for keyframe in keyframes:
        keyframe["time"] = Fraction(keyframe["timeTicks"], 10_000_000)
        for rectangle in keyframe["rectangles"]:
            if rectangle["x"] + rectangle["width"] > width or rectangle["y"] + rectangle["height"] > height:
                raise ValueError("Rectangle keyframes must fit inside the normalized frame")

    def at(seconds):
        left, right = keyframes[0], keyframes[-1]
        for index in range(len(keyframes) - 1):
            if keyframes[index]["time"] <= seconds < keyframes[index + 1]["time"]:
                left, right = keyframes[index:index + 2]
                break
        if seconds >= keyframes[-1]["time"]:
            left = right
        fraction = Fraction(0) if right["time"] == left["time"] else (seconds - left["time"]) / (right["time"] - left["time"])
        selected = np.zeros(shape, bool)
        for start, end in zip(left["rectangles"], right["rectangles"]):
            # Outward rounding includes every pixel touched by the interpolated continuous rectangle.
            x, y, w, h = [start[key] + fraction * (end[key] - start[key]) for key in ["x", "y", "width", "height"]]
            selected[y.numerator // y.denominator:-(-(y + h).numerator // (y + h).denominator),
                     x.numerator // x.denominator:-(-(x + w).numerator // (x + w).denominator)] = True
        return selected
    return at


def load_model(job, manifest):
    import onnxruntime as ort
    with open(job["modelPath"], "rb") as source:
        model = source.read(manifest["bytes"] + 1)
    if len(model) != manifest["bytes"] or hashlib.sha256(model).hexdigest() != manifest["sha256"]:
        raise ValueError("MI-GAN length or SHA-256 does not match the fixed model manifest")
    settings = ort.SessionOptions()
    settings.intra_op_num_threads, settings.inter_op_num_threads = job["cpuThreads"], 1
    session = ort.InferenceSession(model, sess_options=settings, providers=["CPUExecutionProvider"])
    inputs, outputs = session.get_inputs(), session.get_outputs()
    if ([(item.name, item.type) for item in inputs] != [("image", "tensor(uint8)"), ("mask", "tensor(uint8)")]
            or [(item.name, item.type) for item in outputs] != [("result", "tensor(uint8)")]):
        raise ValueError("Unexpected MI-GAN input/output metadata")
    return session


def repair(session, image, mask):
    if not mask.any():
        return image.copy()
    if min(mask.shape) < 3:
        raise ValueError("A nonempty MI-GAN mask requires dimensions of at least three pixels")
    # Exact pinned pipeline: maxpool3 radius1 plus Gaussian5 radius2. Commit remains restricted to caller mask.
    context = np.asarray(Image.fromarray(mask.astype(np.uint8) * 255).filter(ImageFilter.MaxFilter(7))) > 0
    raw = session.run(["result"], {"image": image.transpose(2, 0, 1)[None].copy(), "mask": np.where(context, 0, 255).astype(np.uint8)[None, None]})[0][0].transpose(1, 2, 0)
    if raw.shape != image.shape or raw.dtype != np.uint8:
        raise ValueError("Invalid MI-GAN output tensor")
    return np.where(mask[:, :, None], raw, image)


def encoded_stream(container, width, height, gray=False):
    stream = container.add_stream("ffv1", rate=25)
    stream.width, stream.height, stream.pix_fmt = width, height, "gray" if gray else "bgr0"
    stream.time_base = stream.codec_context.time_base = MILLISECOND
    stream.codec_context.thread_count = 1
    return stream


def emit(container, stream, array, pts, duration, gray=False):
    frame = av.VideoFrame.from_ndarray(array, format="gray" if gray else "rgb24")
    frame.pts, frame.time_base, frame.duration = pts, MILLISECOND, duration
    for packet in stream.encode(frame):
        packet.duration = duration
        container.mux(packet)


def bundle_bytes(directory):
    return sum(path.stat().st_size for path in directory.iterdir() if path.is_file())


def prepare(job, manifest):
    import cv2
    from video_flow import fuse
    cv2.setNumThreads(job["cpuThreads"])
    limits, directory = job["limits"], Path(job["outputDirectory"])
    session = load_model(job, manifest)
    with ExitStack() as stack:
        source = stack.enter_context(av.open(job["inputPath"]))
        selected = video_stream(source)
        if (selected.codec_context.width * selected.codec_context.height > limits["maximumFramePixels"]
                or max(selected.codec_context.width, selected.codec_context.height) > limits["maximumFrameDimension"]):
            raise ValueError("Declared frame dimensions exceed the configured pixel or dimension budget")
        decoded = source.decode(selected)
        first = next(decoded, None)
        if first is None or first.pts is None or first.time_base is None:
            raise ValueError("The video must contain timestamped frames")
        first_image, first_matrix = normalized(first)
        height, width = first_image.shape[:2]
        expected_shape = first_image.shape
        if width * height > limits["maximumFramePixels"] or max(width, height) > limits["maximumFrameDimension"]:
            raise ValueError("Frame pixel budget exceeded")
        first_time = first.pts * first.time_base
        stream_end = ((selected.start_time * selected.time_base if selected.start_time is not None else first_time)
                      + selected.duration * selected.time_base) if selected.duration is not None and selected.duration > 0 else None
        if first_time < 0:
            raise ValueError("Negative video presentation timestamps are unsupported")
        mode = job.get("mode", "manual")
        if mode not in ["manual", "automatic"]:
            raise ValueError("Invalid video removal mode")
        detector = None
        if mode == "automatic":
            from watermark_model import read_verified_checkpoint, WatermarkDetector
            settings = job["detector"]
            detector = WatermarkDetector(read_verified_checkpoint(settings["modelPath"]), settings["profile"], settings["threshold"], job["cpuThreads"])
            detection = {"modelId": detector.manifest["modelId"], "sha256": detector.manifest["sha256"], "profile": detector.profile,
                         "threshold": detector.threshold, "frameCount": 0, "maximumProbability": 0, "meanProbability": 0,
                         "inferenceSeconds": 0, "runtime": detector.runtime}
        else:
            mask_at = masks(job["request"], (height, width))
            detection = None
        audio = audio_info(job["inputPath"], limits, first_time)
        video = stack.enter_context(av.open(str(directory / "video-only.mkv"), "w", format="matroska", options={"avoid_negative_ts": "disabled"}))
        preview = stack.enter_context(av.open(str(directory / "mask-preview.mkv"), "w", format="matroska", options={"avoid_negative_ts": "disabled"}))
        video_output, mask_output = encoded_stream(video, width, height), encoded_stream(preview, width, height, True)
        metadata = stack.enter_context(open(directory / "frame-metadata.json", "w", encoding="utf-8"))
        metadata.write('{"frames":[')
        summary = {"width": width, "height": height, "frameCount": 0, "selectedPixelCount": 0, "editedPixelCount": 0,
                   "flowContributedPixelCount": 0, "firstVideoSeconds": float(first_time), "videoDurationSeconds": 0, "audio": audio,
                   "miganModelId": manifest["modelId"], "miganSha256": manifest["sha256"], "method": "image-MI-GAN-plus-gated-optical-flow",
                   "runtime": {name: importlib.metadata.version(name) for name in ["av", "onnxruntime", "opencv-python"]}}
        summary.update({"mode": mode, "detection": detection})
        previous, current, previous_tick, read_count = None, None, None, 0
        timeline_hash = hashlib.sha256()

        def state(frame, image=None):
            nonlocal previous_tick, read_count
            image, matrix = normalized(frame) if image is None else (image, first_matrix)
            if image.shape != expected_shape or matrix != first_matrix:
                raise ValueError("Dynamic frame dimensions or display matrices are unsupported")
            if frame.pts is None or frame.time_base is None:
                raise ValueError("Frame presentation timestamps are required")
            seconds = frame.pts * frame.time_base
            tick = milliseconds(seconds)
            if previous_tick is not None and tick <= previous_tick:
                raise ValueError("Missing, duplicate, backwards or colliding 1 ms video timestamps")
            previous_tick = tick
            read_count += 1
            if read_count > limits["maximumFrames"] or read_count * width * height > limits["maximumTotalPixels"] or float(seconds - first_time) > limits["maximumDurationSeconds"]:
                raise ValueError("Video frame, cumulative pixel or duration budget exceeded")
            observed = None
            if detector is not None:
                mask, observed = detector.predict(Image.fromarray(image))
                detection["frameCount"] += 1
                detection["maximumProbability"] = max(detection["maximumProbability"], observed["maximumProbability"])
                detection["meanProbability"] += observed["meanProbability"]
                detection["inferenceSeconds"] += observed["inferenceSeconds"]
            else:
                mask = mask_at(seconds - first_time)
            return {"image": image, "mask": mask, "repair": repair(session, image, mask), "pts": tick, "sourcePts": frame.pts,
                    "sourceTimeBase": str(frame.time_base), "seconds": seconds, "sourceDuration": frame.duration * frame.time_base,
                    "detection": observed}

        def output(current, following):
            duration = current["sourceDuration"]
            if duration <= 0 and following is not None:
                duration = following["seconds"] - current["seconds"]
            if duration <= 0 and following is None and stream_end is not None:
                duration = stream_end - current["seconds"]
            if duration <= 0:
                raise ValueError("A positive final frame duration is required; no artificial CFR duration is inferred")
            duration_tick = milliseconds(duration)
            if duration_tick < 1 or float(current["seconds"] + duration - first_time) > limits["maximumDurationSeconds"]:
                raise ValueError("Frame duration cannot be represented within the configured timeline")
            result, diagnostics = fuse(current, [value for value in [previous, following] if value is not None], job["flow"])
            emit(video, video_output, result, current["pts"], duration_tick)
            emit(preview, mask_output, current["mask"].astype(np.uint8) * 255, current["pts"], duration_tick, True)
            timeline_hash.update(f'{current["pts"]}:{duration_tick}\n'.encode("ascii"))
            changed = int(np.any(result != current["image"], axis=2)[current["mask"]].sum())
            if summary["frameCount"] == 0:
                Image.fromarray(current["image"]).save(directory / "original-frame.png")
                Image.fromarray(current["mask"].astype(np.uint8) * 255).save(directory / "mask-frame.png")
                Image.fromarray(result).save(directory / "result-frame.png")
            record = {"sourcePts": current["sourcePts"], "sourceTimeBase": current["sourceTimeBase"], "outputPtsMilliseconds": current["pts"],
                      "outputDurationMilliseconds": duration_tick, "selectedPixels": int(current["mask"].sum()), "editedPixels": changed, "neighbors": diagnostics}
            if current["detection"] is not None:
                record["detection"] = {key: current["detection"][key] for key in ["maximumProbability", "meanProbability", "inferenceSeconds", "selectedPixelCount"]}
            if summary["frameCount"]:
                metadata.write(",")
            json.dump(record, metadata, separators=(",", ":"))
            metadata.flush()
            summary["frameCount"] += 1
            summary["selectedPixelCount"] += record["selectedPixels"]
            summary["editedPixelCount"] += changed
            summary["flowContributedPixelCount"] += sum(item["contributedPixels"] for item in diagnostics)
            summary["videoDurationSeconds"] = float(current["seconds"] + duration - first_time)
            if bundle_bytes(directory) > limits["maximumOutputBytes"]:
                raise ValueError("Video and preview output byte budget exceeded")

        current = state(first, first_image)
        first = first_image = None
        # Only previous/current/following states are retained; all output metadata is written incrementally.
        for frame in decoded:
            following = state(frame)
            output(current, following)
            previous, current = current, following
        output(current, None)
        if detection is not None:
            detection["meanProbability"] /= detection["frameCount"]
        summary["timelineSha256"] = timeline_hash.hexdigest()
        for container, stream in [(video, video_output), (preview, mask_output)]:
            for packet in stream.encode(None):
                container.mux(packet)
        metadata.write('],"summary":')
        json.dump(summary, metadata, separators=(",", ":"))
        metadata.write("}")
    if bundle_bytes(directory) > limits["maximumOutputBytes"]:
        raise ValueError("Completed video and preview output exceed the byte budget")
    (directory / "summary.json").write_text(json.dumps(summary), encoding="utf-8")


def validate(job):
    directory, limits = Path(job["outputDirectory"]), job["limits"]
    summary = json.loads((directory / "summary.json").read_text(encoding="utf-8"))
    with ExitStack() as stack:
        source = stack.enter_context(av.open(job["inputPath"]))
        result = stack.enter_context(av.open(str(directory / "result.mkv")))
        mask = stack.enter_context(av.open(str(directory / "mask-preview.mkv")))
        original_frames, output_frames, mask_frames = source.decode(video_stream(source)), result.decode(video_stream(result)), mask.decode(video_stream(mask))
        count, timeline_hash = 0, hashlib.sha256()
        for original in original_frames:
            actual, selected = next(output_frames, None), next(mask_frames, None)
            if actual is None or selected is None:
                raise ValueError("The encoded output was truncated or dropped frames")
            baseline, _ = normalized(original)
            output, matrix = normalized(actual)
            binary = selected.to_ndarray(format="gray")
            if matrix != IDENTITY or output.shape != baseline.shape or binary.shape != baseline.shape[:2] or not np.isin(binary, [0, 255]).all():
                raise ValueError("Invalid encoded output geometry or mask")
            if actual.pts * actual.time_base != milliseconds(original.pts * original.time_base) * MILLISECOND or selected.pts * selected.time_base != actual.pts * actual.time_base:
                raise ValueError("The output changed presentation timestamps")
            duration = actual.duration * actual.time_base / MILLISECOND
            if duration.denominator != 1 or selected.duration * selected.time_base != actual.duration * actual.time_base:
                raise ValueError("The output changed frame durations")
            timeline_hash.update(f'{milliseconds(actual.pts * actual.time_base)}:{int(duration)}\n'.encode("ascii"))
            if np.any(output[binary == 0] != baseline[binary == 0]):
                raise ValueError("The lossless output changed pixels outside the final binary mask")
            count += 1
        if (next(output_frames, None) is not None or next(mask_frames, None) is not None or count != summary["frameCount"]
                or timeline_hash.hexdigest() != summary["timelineSha256"]):
            raise ValueError("The output frame counts do not match")
    actual_audio = audio_info(directory / "result.mkv", limits, Fraction(str(summary["firstVideoSeconds"])), allow_unlabelled_multichannel=True)
    if len(actual_audio) != len(summary["audio"]):
        raise ValueError("The output lost or added audio tracks")
    for expected, actual in zip(summary["audio"], actual_audio):
        if actual["sourceCodec"] != "pcm_f32le" or any(expected[key] != actual[key] for key in ["sampleRate", "channels", "samples", "float32Sha256", "firstMilliseconds"]):
            raise ValueError("The output changed decoded float32 audio samples, channel order/count or presentation offset")
    if bundle_bytes(directory) - (directory / "video-only.mkv").stat().st_size > limits["maximumOutputBytes"]:
        raise ValueError("The complete output bundle exceeds its byte budget")
    print(json.dumps(summary), flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("operation", choices=["prepare", "validate"])
    parser.add_argument("job")
    args = parser.parse_args()
    manifest = json.loads(Path(__file__).with_name("video-modelmanifest.json").read_text(encoding="utf-8"))
    for package in ["av", "onnxruntime", "opencv-python"]:
        if importlib.metadata.version(package) != manifest[package]:
            raise RuntimeError(f"Expected {package}=={manifest[package]}")
    job = json.loads(Path(args.job).read_text(encoding="utf-8-sig"))
    if args.operation == "prepare":
        prepare(job, manifest)
    else:
        validate(job)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(json.dumps({"error": type(error).__name__, "message": str(error)}), file=sys.stderr, flush=True)
        sys.exit(2)
