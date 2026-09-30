"""Builds one lesson from its lesson.yaml: the real program in the browser,
started where the lesson starts, with a voice that explains and the learner doing every
click themselves, and the page of the guide that walks the same steps in text and images.

    uv run --with-requirements web/lessons/requirements.txt web/lessons/lesson.py [lesson.yaml ...] [stage ...] [--elevenlabs]

Stages: guide, zustand, voice, site (default: all four in that order), for the lessons named
or else every published one. The voice drafts with the Mac's `say` unless --elevenlabs asks
for the host's own.
"""

import argparse
import base64
import hashlib
import io
import json
import os
import shlex
import shutil
import subprocess
import tarfile
from functools import partial
from html import escape
from itertools import accumulate, groupby, pairwise
from operator import itemgetter
from pathlib import Path
from urllib.request import urlretrieve

import numpy as np
import pyloudnorm
import soundfile
import yaml
from dotenv import load_dotenv
from elevenlabs import ElevenLabs

ROOT = Path(__file__).resolve().parents[2]
CACHE = Path(os.environ.get("UMSATZ_LESSON_CACHE", Path.home() / ".cache" / "umsatz-lessons"))
EXAMPLES = "https://github.com/rotmanjanez/Umsatzschaetzung/releases/download/beispiel-rechnungen-2025/beispiel-rechnungen-2025.zip"

# The guide's example case is played by these lessons in turn, each starting where the one
# before stops; a lesson not among them starts on an empty program.
GUIDE = ["pruefung", "rechnungen", "korrektur", "zuordnung", "sortiment", "einkaeufe", "bericht"]
SCRIPT = ROOT / "web" / "docs" / "shots" / "guide.jsonl"
EXERCISES = ROOT / "web" / "docs" / "pages" / "uebungen.md"
APP = "https://app.umsatzschaetzung.amtstools.de/lektionen/"


class Lesson:
    def __init__(self, path: Path):
        self.spec = yaml.safe_load(path.read_text())
        self.out = ROOT / "web" / "lessons" / "out" / path.parent.name
        self.path = path
        self.beats = [written(c["title"], b) for c in self.spec["chapters"] for b in c["beats"] if played(b)]


# A beat that only writes or photographs belongs to the guide alone.
def played(spec: dict) -> bool:
    return any(key in spec for key in ("say", "card", "click", "type"))


# A click or type is the learner's: it lets through what the beat acts on and is done
# once a clicked row, tab or option is chosen or a typed field reads right, unless the beat
# says otherwise.
def written(chapter: str, spec: dict) -> dict:
    target = spec["type"]["at"] if "type" in spec else spec.get("click")
    done = spec.get("done")
    if done is None and target:
        done = ({"at": target, "text": [str(spec["type"]["text"]), *map(str, spec.get("accept", []))]}
                if "type" in spec else {"at": target, "selected": True})
    return {"allow": spec.get("allow", [target] if target else []), "done": done,
            "chapter": chapter, "say": spec.get("say", "").strip(), "card": spec.get("card"),
            "note": spec.get("note", ""), "task": spec.get("task", ""), "hint": spec.get("hint", ""),
            "show": spec.get("show"), "through": spec.get("through"), "hold": spec.get("hold", 0.0),
            "do": instead(spec), "undo": spec.get("undo", [])}


# What the page does in the learner's place when the slider passes a beat they act in: what the
# headless program does, without its photographs, its waiting and its second windows.
def instead(spec: dict) -> list[dict]:
    if "click" not in spec and "type" not in spec:
        return []
    return [{k: v for k, v in s.items() if k != "window"} for s in steps(spec) if s["do"] not in ("shot", "wait", "pick")]


# --- zustand -------------------------------------------------------------------------

# A lesson starts where the lessons before it in the guide stop, so both show the same case: the
# headless program plays the guide once, and the stores are kept wherever one of the lessons
# starts.
def zustand(lessons: list[Lesson]):
    examples()
    starts = {}
    for ep in lessons:
        name = ep.path.parent.name
        starts.setdefault(GUIDE.index(name) if name in GUIDE else 0, []).append(ep)
    lines = []
    for i in range(max(starts) + 1):
        lines += replayed(GUIDE[i - 1]) if i else []
        lines += [{"do": "keep", "to": str(ep.out / "zustand")} for ep in starts.get(i, [])]
    out = ROOT / "web" / "lessons" / "out"
    script = out / "zustand.jsonl"
    script.write_text("\n".join(json.dumps(l, ensure_ascii=False) for l in lines) + "\n")
    w, h = lessons[0].spec["window"]
    subprocess.run(["dotnet", "run", "--project", ROOT / "tools" / "headless", "-c", "Release", "--",
                    script, "--width", str(w), "--height", str(h), "--out", out / "headless",
                    "--readings", CACHE / "readings"], cwd=CACHE, check=True)


def replayed(name: str) -> list[dict]:
    return [rooted(s) for beat in beats(name) for s in steps(beat) if s["do"] != "shot"]


# The program runs in the cache, so a file the repository holds is named from its root.
def rooted(step: dict) -> dict:
    if step["do"] != "pick":
        return step
    return {**step, "files": [str(ROOT / f) if (ROOT / f).exists() else f for f in step["files"]]}


def beats(name: str) -> list[dict]:
    spec = yaml.safe_load((ROOT / "web" / "lessons" / name / "lesson.yaml").read_text())
    return [beat for chapter in spec["chapters"] for beat in chapter["beats"]]


# What the headless program does for a beat: the learner's click or typing, or `act`
# where a plain click does not fit, and further rounds only where settling is not enough.
def steps(spec: dict) -> list[dict]:
    if "act" in spec:
        acts = spec["act"]
    elif "click" in spec:
        acts = [{"do": spec.get("via", "click"), "at": spec["click"]}]
    elif "type" in spec:
        acts = [{"do": "type", "at": spec["type"]["at"], "text": str(spec["type"]["text"])}, {"do": "focus"}]
    else:
        return []
    return acts + spec.get("then", []) + ([{"do": "wait", "rounds": spec["rounds"]}] if "rounds" in spec else [])


def examples():
    target = CACHE / "beispiel-rechnungen"
    if not target.exists():
        shutil.unpack_archive(urlretrieve(EXAMPLES)[0], target, "zip")


# --- guide ---------------------------------------------------------------------------

# The guide walks the same steps as the lessons: a lesson's page is written from its beats'
# `text` and `shot`, and the guide's script, which takes the images, from the steps of all
# of them, so the pages, the images and the lessons cannot drift apart.
def guide(ep: Lesson):
    spec = ep.spec.get("guide")
    if not spec or "title" not in spec:
        print("no guide in", ep.path)
        return
    parts = [f"<!-- written from {ep.path.relative_to(ROOT)} -->", f"# {spec['title']}", spec.get("intro", "")]
    if ep.path.parent.name in dict(published()):
        parts.append(f'[Übung {ep.spec["number"]}: {ep.spec["title"]} <span>Im Programm mitmachen, direkt im Browser</span>]'
                     f'({APP}{ep.path.parent.name}/){{ .us-exercise }}')
    for chapter in ep.spec["chapters"]:
        blocks, texts = [], []
        for beat in chapter["beats"]:
            if "text" in beat:
                texts.append(beat["text"].strip())
            if "shot" in beat:
                blocks.append(figure(texts, beat["shot"]))
                texts = []
        if blocks or texts:
            parts += [f"## {chapter['title']}", *blocks, *texts]
    parts.append(spec.get("end", ""))
    (ROOT / spec["page"]).write_text("\n\n".join(p.strip() for p in parts if p.strip()) + "\n")
    script()
    exercises()


# The lessons published beside the app, as the deploy names them in LESSONS; without it,
# every lesson in a course.
def published() -> list[tuple[str, dict]]:
    specs = {p.parent.name: yaml.safe_load(p.read_text()) for p in (ROOT / "web" / "lessons").glob("*/lesson.yaml")}
    names = os.environ.get("LESSONS", "").split() or [n for n, s in specs.items() if "course" in s]
    return sorted(((n, specs[n]) for n in names), key=lambda l: l[1]["number"])


# The documentation's page of the exercises lists the published lessons by course.
def exercises():
    courses = {}
    for name, spec in published():
        courses.setdefault(spec["course"], []).append((name, spec))
    listed = "\n".join(
        f'<h2>{escape(course)}</h2>\n<ol class="us-exercises">\n'
        + "".join(f'<li><a href="{APP}{name}/"><span>{spec["number"]}</span><b>{escape(spec["title"])}</b>'
                  f'{escape(spec["summary"])}</a></li>\n' for name, spec in lessons)
        + "</ol>" for course, lessons in courses.items())
    page = (ROOT / "web" / "lessons" / "uebungen.md").read_text()
    EXERCISES.write_text(page.replace("{courses}", listed))


# The text beside its image, or above it where the image wants the whole width.
def figure(texts: list[str], shot: dict) -> str:
    image = f"![{shot['alt']}](img/{shot['name']}.png)" + (f'{{ width="{shot["width"]}" }}' if "width" in shot else "")
    if not texts or shot.get("below"):
        return "\n\n".join([*texts, image])
    text = "\n\n".join(texts)
    return f'<div class="us-side" markdown>\n<div markdown>\n\n{text}\n\n</div>\n\n{image}\n\n</div>'


def script():
    lines = ["// The guide's example case, created, filled with all 106 invoices, the three open ones corrected,",
             "// the open positions mapped, the assortment built, the report shown.", ""]
    for name in GUIDE:
        lines.append(f"// written from web/lessons/{name}/lesson.yaml")
        lines += [jsonl(step) for beat in beats(name) for step in steps(beat) + photo(beat)] + [""]
    SCRIPT.write_text("\n".join(lines))


def photo(beat: dict) -> list[dict]:
    shot = beat.get("shot")
    return [{"do": "shot", **{k: shot[k] for k in ("name", "window", "at", "trim", "clip") if k in shot}}] if shot else []


def jsonl(value) -> str:
    if isinstance(value, dict):
        return "{ " + ", ".join(f"{json.dumps(k)}: {jsonl(v)}" for k, v in value.items()) + " }"
    if isinstance(value, list):
        return "[" + ", ".join(map(jsonl, value)) + "]"
    return json.dumps(value, ensure_ascii=False)


# --- voice ---------------------------------------------------------------------------

# The host speaks with this ElevenLabs voice, but only with --elevenlabs; otherwise the
# Mac's own German voice drafts every sentence for free, so a script can be heard and
# edited before a take costs anything.
ELEVEN_VOICE = "J5U94vRbS9drxnawJcoc"
ELEVEN_MODEL = "eleven_v3"
DRAFT_VOICE = "Anna"


# With ElevenLabs a chapter is spoken in one take, so the voice keeps one melody from its
# first sentence to its last; sentence by sentence it starts afresh every time. The take
# is cut between the beats into one clip per sentence, and a sentence keeps its clip until
# its words change. A changed sentence is spoken again with the sentences around it, so it
# sounds as it would within the chapter, and only its own part is kept: a changed word
# costs a few sentences, not the chapter.
def voice(ep: Lesson, eleven: bool):
    chapters = [[b["say"] for b in group]
                for _, group in groupby((b for b in ep.beats if b["say"]), itemgetter("chapter"))]
    ton = ep.out / "ton"
    shutil.rmtree(ton, ignore_errors=True)
    ton.mkdir(parents=True)
    says = [say for chapter in chapters for say in chapter]
    paths = spoken(chapters, ton) if eleven else [drafted(say, ton) for say in says]
    clips = {say: f"ton/{path.name}" for say, path in zip(says, paths)}
    (ep.out / "stimme.json").write_text(json.dumps(clips, ensure_ascii=False, indent=1))


# Every clip the host speaks lives in one store and nowhere else, so it is paid for once,
# on whichever machine speaks it first: a run fetches the lesson's clips from the store and
# leaves there each take's clips as soon as they are paid for. The store is a folder
# reached over SSH, `user@host:folder` in UMSATZ_LESSON_STORE, with the key in
# UMSATZ_LESSON_STORE_KEY. A clip is named by the hash of what it says, so a stored clip
# never changes and is never stale.
def store(script: str, data: bytes = b"") -> bytes:
    target = os.environ.get("UMSATZ_LESSON_STORE")
    if not target:
        raise SystemExit("--elevenlabs needs the store in UMSATZ_LESSON_STORE")
    host, folder = target.split(":", 1)
    key = os.environ.get("UMSATZ_LESSON_STORE_KEY")
    ssh = ["ssh", "-o", "BatchMode=yes", *(["-i", os.path.expanduser(key), "-o", "IdentitiesOnly=yes"] if key else [])]
    remote = f"mkdir -p {shlex.quote(folder)} && cd {shlex.quote(folder)} && {script}"
    return subprocess.run([*ssh, host, remote], input=data, stdout=subprocess.PIPE, check=True).stdout


def fetched(names: list[str], into: Path):
    listed = "".join(f"{name}\n" for name in dict.fromkeys(names)).encode()
    archive = store('while read -r f; do [ -f "$f" ] && echo "$f"; done | tar -cf - -T -', listed)
    with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
        tar.extractall(into, filter="data")
        print(f"store: fetched {len(tar.getnames())} of {len(listed.split())} clips", flush=True)


# Unpacked beside the store and moved in, so a broken upload never leaves a clip half there.
def left(paths: list[Path]):
    archive = io.BytesIO()
    with tarfile.open(fileobj=archive, mode="w") as tar:
        for path in dict.fromkeys(paths):
            tar.add(path, arcname=path.name)
    store('t=$(mktemp -d .up.XXXXXX) && tar -xf - -C "$t" && mv "$t"/* . && rmdir "$t"', archive.getvalue())
    print(f"store: left {len(set(paths))} clips", flush=True)


# Where most of a chapter is new it is taken whole; otherwise every run of changed sentences
# is taken with its neighbours. Only the missing clips are cut from a take, so a sentence
# that is stored never changes its sound.
def spoken(chapters: list[list[str]], ton: Path) -> list[Path]:
    paths = [[ton / f"{digest([ELEVEN_MODEL, ELEVEN_VOICE, say])}.mp3" for say in says] for says in chapters]
    fetched([path.name for chapter in paths for path in chapter], ton)
    unspoken = [say for says, chapter in zip(chapters, paths) for say, path in zip(says, chapter) if not path.exists()]
    if unspoken and not os.environ.get("ELEVENLABS_API_KEY"):
        raise SystemExit("not in the store, and no ELEVENLABS_API_KEY to speak it:\n" + "\n".join(unspoken))
    client = ElevenLabs(api_key=os.environ["ELEVENLABS_API_KEY"]) if unspoken else None
    for says, chapter in zip(chapters, paths):
        missing = [i for i, path in enumerate(chapter) if not path.exists()]
        if not missing:
            continue
        for lo, hi in [(0, len(says))] if 2 * len(missing) > len(says) else windows(missing, len(says)):
            for path, (samples, rate) in zip(chapter[lo:hi], take(client, says[lo:hi])):
                if not path.exists():
                    kept(path, samples, rate)
        left([chapter[i] for i in missing])
    return [path for chapter in paths for path in chapter]


# Runs of missing sentences, each with one sentence before and after; runs at most two
# sentences apart share a take, which costs no more than their two.
def windows(missing: list[int], n: int) -> list[tuple[int, int]]:
    runs = []
    for i in missing:
        if runs and i - runs[-1][1] <= 3:
            runs[-1][1] = i
        else:
            runs.append([i, i])
    return [(max(a - 1, 0), min(b + 2, n)) for a, b in runs]


# Each sentence is cut from the take in the pause before its first letter, whose time the
# model reports itself.
def take(client, says: list[str]) -> list[tuple[np.ndarray, int]]:
    text = " ".join(says)
    print(f"elevenlabs: {len(text)} chars, {len(says)} sentences", flush=True)
    answer = client.text_to_speech.convert_with_timestamps(
        ELEVEN_VOICE, text=text, model_id=ELEVEN_MODEL, language_code="de", output_format="mp3_44100_192")
    letters = answer.alignment.character_start_times_seconds
    samples, rate = soundfile.read(io.BytesIO(base64.b64decode(answer.audio_base_64)))
    samples = level(samples, rate)
    bounds = [0.0]
    for offset in accumulate(len(say) + 1 for say in says[:-1]):
        start = letters[offset]
        bounds.append(max(bounds[-1], quietest(samples, rate, start - 0.45, start + 0.1)))
    bounds.append(len(samples) / rate)
    return [(samples[int(a * rate):int(b * rate)], rate) for a, b in pairwise(bounds)]


def kept(path: Path, samples: np.ndarray, rate: int):
    soundfile.write(path, faded(samples, rate), rate, format="MP3")


# A clip cut from a take starts and ends mid-wave; 20 ms of ramp on either end keeps it from clicking.
def faded(samples: np.ndarray, rate: int, seconds: float = 0.02) -> np.ndarray:
    n = min(int(seconds * rate), len(samples) // 2)
    ramp = np.linspace(0, 1, n).reshape(-1, *[1] * (samples.ndim - 1))
    out = samples.copy()
    out[:n] *= ramp
    out[len(out) - n:] *= ramp[::-1]
    return out


# Drafts cost nothing and are never stored; they are kept here only to spare `say` the time.
def drafted(text: str, ton: Path) -> Path:
    path = CACHE / "tts" / "drafts" / f"{digest([DRAFT_VOICE, text])}.mp3"
    if not path.exists():
        path.parent.mkdir(parents=True, exist_ok=True)
        aiff = path.with_suffix(".aiff")
        subprocess.run(["say", "-v", DRAFT_VOICE, "-o", aiff, text], check=True)
        samples, rate = soundfile.read(aiff)
        kept(path, level(samples, rate), rate)
        aiff.unlink()
    return Path(shutil.copy(path, ton))


# The middle of the quietest 30 ms between start and end.
def quietest(samples: np.ndarray, rate: int, start: float, end: float) -> float:
    a, window = int(max(start, 0) * rate), int(0.03 * rate)
    energy = np.convolve(np.abs(samples[a:int(end * rate)]), np.ones(window), "valid")
    return (a + energy.argmin() + window // 2) / rate


# Voices come out at very different levels; every take is brought to the same
# loudness as a whole, so a chapter keeps its dynamics and no lesson is quieter than the last.
def level(samples: np.ndarray, rate: int, loudness: float = -20.0, ceiling: float = -1.0) -> np.ndarray:
    gain = min(loudness - pyloudnorm.Meter(rate).integrated_loudness(samples),
               ceiling - 20 * np.log10(np.abs(samples).max()))
    return samples * 10 ** (gain / 20)


def digest(value) -> str:
    return hashlib.sha1(json.dumps(value).encode()).hexdigest()


# --- site ----------------------------------------------------------------------------

# The lessons are one site inside the web app: the page once, a folder per lesson with its
# script and the stores it starts from, and the clips of every lesson in one `ton/`, where a
# sentence two lessons share is one file. The page is the web app itself, its runtime, its
# services and its weights taken from the app one level up, the services started on the stores
# kept where the lesson begins, with the lesson played over it: every beat speaks its clip,
# cards and spotlights are drawn over the live interface, and a click or type waits for the
# learner. To try it, put the site into a published app's wwwroot.
SITE = ROOT / "web" / "lessons" / "out" / "lektionen"
PAGE = ROOT / "web" / "lessons" / "page"


def site(lessons: list[Lesson]):
    shutil.copytree(PAGE, SITE, dirs_exist_ok=True, ignore=shutil.ignore_patterns("index.html"))
    (SITE / "fonts").mkdir(exist_ok=True)
    for f in (ROOT / "web" / "docs" / "pages" / "assets" / "fonts").glob("sora-latin-*.woff2"):
        shutil.copy(f, SITE / "fonts")
    for f in ("dachs-zu.png", "dachs-halb.png", "dachs-offen.png", "favicon.svg"):
        shutil.copy(ROOT / "web" / "docs" / "pages" / "assets" / f, SITE)
    page = (PAGE / "index.html").read_text().replace("<head>", '<head>\n<base href="../">', 1)
    (SITE / "ton").mkdir(exist_ok=True)
    for ep in lessons:
        placed(ep, page)
    onward()


def placed(ep: Lesson, page: str):
    target = SITE / ep.path.parent.name
    shutil.rmtree(target, ignore_errors=True)
    target.mkdir(parents=True)
    if (ep.out / "zustand").exists():
        shutil.copytree(ep.out / "zustand", target / "zustand")
    (target / "index.html").write_text(page)
    spoken = ep.out / "stimme.json"
    clips = json.loads(spoken.read_text()) if spoken.exists() else {}
    for clip in clips.values():
        shutil.copy(ep.out / clip, SITE / clip)
    beats = [{**b, "audio": clips.get(b["say"])} for b in ep.beats]
    lesson = {key: ep.spec[key] for key in ("course", "number", "title", "summary", "done")}
    (target / "lesson.json").write_text(json.dumps({**lesson, "beats": beats}, ensure_ascii=False, indent=1))
    print(target / "index.html")


# Every lesson ends on a way on to the next one in its course.
def onward():
    lessons = sorted(((p, json.loads(p.read_text())) for p in SITE.glob("*/lesson.json")), key=lambda l: l[1]["number"])
    courses = {}
    for path, lesson in lessons:
        courses.setdefault(lesson["course"], []).append((path, lesson))
    for course in courses.values():
        for (path, lesson), after in zip(course, course[1:] + [None]):
            lesson["next"] = after and {"href": f"{after[0].parent.name}/", "title": after[1]["title"]}
            path.write_text(json.dumps(lesson, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    load_dotenv()
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("items", nargs="*", metavar="lesson.yaml|stage")
    parser.add_argument("--elevenlabs", action="store_true")
    args = parser.parse_args()
    each = {"guide": guide, "voice": partial(voice, eleven=args.elevenlabs)}
    course = {"zustand": zustand, "site": site}
    stages = [i for i in args.items if i in each or i in course] or ["guide", "zustand", "voice", "site"]
    paths = [Path(i).resolve() for i in args.items if i not in each and i not in course]
    lessons = [Lesson(p) for p in paths or (ROOT / "web" / "lessons" / n / "lesson.yaml" for n, _ in published())]
    for lesson in lessons:
        lesson.out.mkdir(parents=True, exist_ok=True)
    for stage in stages:
        print(f"== {stage}", flush=True)
        playable = [l for l in lessons if stage == "guide" or l.beats]
        if stage in course:
            course[stage](playable)
        else:
            for lesson in playable:
                each[stage](lesson)
