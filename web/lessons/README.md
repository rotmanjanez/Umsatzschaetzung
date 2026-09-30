# Lessons

Interactive exercises for people who have never used the program and are not at home
with a computer. Each lesson is one script, played on the real program in the browser:
a voice explains, spotlights fall on the controls it speaks about, and the learner does
every click themselves.

    uv run --with-requirements web/lessons/requirements.txt web/lessons/lesson.py web/lessons/zuordnung/lesson.yaml

Without a lesson named, every published one is built.

The lessons are one site, `web/lessons/out/lektionen/`: the page once, a folder per lesson
with its `lesson.json` and the stores it starts from, and the clips of every lesson in
`ton/`. The page takes its weights from the web app's `models/` and `ort/` one level up, as
it does beside the app at `app.umsatzschaetzung.amtstools.de/lektionen/<lesson>/`; `site`
links them into `out/`. Each lesson ends on a way on to the next one in its course. The
documentation lists them on its page `uebungen.md`, which `guide` writes from `uebungen.md`
here with the lessons named in `LESSONS` by `course`, in the order of their `number`, or
every lesson in a course where `LESSONS` is not set; `lektionen/` itself leads there. The
page shows Didi, the badger who speaks in them, drawn by Maxie Bichmann (bichmann.net), and
the guide's page of a lesson listed there opens with a link to it. It needs a web server, `python3 -m http.server -d web/lessons/out`
will do, with the lesson at `localhost:8000/lektionen/zuordnung/`; a page opened as a
file cannot load WebAssembly.

`deploy-web` publishes the lessons named in its `LESSONS`: the `zustand` job keeps their
stores, the `lessons` job fetches their clips from the store and builds the site beside it,
and `lessons-deploy` puts the stores into the site and deploys it into the app. A sentence
missing from the store fails the `lessons` job, so a lesson is spoken with
`voice --elevenlabs` before it is added there.

## Stages

Each stage can be run alone, named after the lessons:

| stage | does | needs |
|---|---|---|
| `guide` | writes the lesson's page of the guide (`guide.page`), the documentation's `uebungen.md` and the guide's script `web/docs/shots/guide.jsonl` from the steps of every lesson in `GUIDE`, so the screenshots are taken on the same steps | nothing |
| `zustand` | plays the guide once on the real program, headlessly, and keeps the stores in each lesson's `zustand/` where it starts | .NET, the example invoices (fetched on first use) |
| `voice` | speaks every sentence into `ton/`: as a draft with the Mac's `say`, or with `--elevenlabs` in the host's voice, one take per chapter cut between the beats | macOS; with `--elevenlabs`, `ELEVENLABS_API_KEY` in the environment or in `.env` |
| `site` | publishes the program once into `lektionen/`, with each lesson's `zustand/`, where one was kept, and `lesson.json` in `lektionen/<lesson>/` and its clips in `lektionen/ton/` | .NET with the `wasm-tools` workload |

The guide's pages and its script are not checked in: `deploy-web` writes them with `guide`
before it takes the screenshots and builds the documentation. Locally, before either:

    uv run --with-requirements web/lessons/requirements.txt web/lessons/lesson.py web/lessons/*/lesson.yaml guide

Text, pacing and what the learner is asked to do are read from `lesson.yaml` by `site`,
so a changed hint needs only `site`, a changed sentence `voice site`. Only a change to
what is clicked or typed, in the lesson or one before it in `GUIDE`, needs `zustand` again.

A take is cut into one clip per sentence, and a sentence keeps its clip until its words
change. A changed sentence is taken again with one sentence on either side, and only its
own clip is kept, so a changed word costs three sentences, not the chapter; a chapter more
than half new is taken whole. Every request prints its length.

Every clip lives in one store and nowhere else, so it is paid for once, on whichever
machine speaks it first: `voice --elevenlabs` fetches the lesson's clips from the store
and leaves each take's clips there as soon as they are paid for. The store is a folder
reached over SSH, set in the environment or `.env`; without it `--elevenlabs` stops before
it pays for anything:

    UMSATZ_LESSON_STORE=hosting216134@hosting216134.a151b.netcup.net:umsatzschaetzung-stimme
    UMSATZ_LESSON_STORE_KEY=~/.ssh/umsatzschaetzung_deploy

A clip is named by the hash of its model, voice and sentence, so a stored clip never changes
and is never stale.

Drafts cost nothing, so a script is heard and edited with `voice site` and spoken for real
with `voice site --elevenlabs` once it stands. They are never stored; `~/.cache/umsatz-lessons`
keeps them only to spare `say` the time, and also holds the example invoices and the
recorded OCR readings, so only the first `zustand` run reads the 106 scans.
`UMSATZ_LESSON_CACHE` moves it.

## Voice

One voice speaks through every lesson, so every lesson sounds like the last: `ELEVEN_VOICE`
in `lesson.py`. A changed voice is spoken anew in every lesson on its next
`voice --elevenlabs` run.

## Lesson

The guide's example case is played by the lessons in `GUIDE` in `lesson.py`, in turn: a
lesson starts where the one before it stops, so it shows the same case as the guide, and
one not in `GUIDE` starts on an empty program. A lesson with nothing played, like
`rechnungen`, is the guide's alone and has only the `guide` stage. It is split into chapters, each chapter into beats. A beat is
one sentence or a few, together with what happens on screen:

```yaml
- say: Klicken Sie auf die Zeile „2022 Domina trocken“.
  click: { starts: "2022 Domina" }   # a target, as in the headless scripts
  via: select                        # how the headless run performs it (default: click)
  rounds: 20                         # let the interface settle afterwards
  task: Klicken Sie auf „2022 Domina trocken 0,75 l“.   # what the card says to do
  hint: Es ist die achte Zeile.
  allow: [{ starts: "2022 Domina", up: DataGridRow }]  # anywhere on the row counts
```

| key | meaning |
|---|---|
| `say` | what the voice says; also the subtitle |
| `card` | a full-screen card over the program: `kicker`, `title`, `text`, `list`, `pad: true` for the trackpad |
| `click` | the learner clicks there |
| `act` | headless steps that perform the click, where a plain click does not fit (a tab) |
| `type` | `{ at, text }`: the learner clicks into a field and types |
| `show` | spotlights a control without touching it; `through` stretches the spotlight over every shown control that matches a second target |
| `note` | a line on the card for the beat |
| `hold` | seconds to wait after the sentence |
| `task`, `hint` | the short line on the card while the program waits for the learner (default: `say`), and what a miss is told |
| `allow` | the targets whose presses and keys reach the program in that beat (default: the `click` or `type` target) |
| `text` | markdown for the guide page; everything written up to a `shot` stands beside its image |
| `shot` | `{ name, at, trim?, clip?, alt, width?, below? }`: the guide's image after the beat's step, beside the text or, with `below`, under it |
| `undo` | steps that take back what going back over the beat does not by itself: the case and rules, the invoices open, tabs, rows, options and fields are put back as they were; a form the click opened is not |
| `done` | what the program shows once the learner has done it: `{ at }` is there, with `selected: true` its row, tab or option is chosen, with `text: [...]` its field reads one of them. Default: a click chooses its target, a typed field reads the `type` text or one of `accept` |

The window size is set once per lesson (`window`).

A beat with `say`, `card`, `click` or `type` is played; one with only `text` and `shot`
is the guide's alone, and one with only `say` or `card` the lesson's. What is clicked or
typed happens in both, so the guide's images and the lesson start the next one from the
same case. `guide` names the page with `title`, `intro` and `end`; a chapter with
nothing written becomes no heading there.

## Page

The page is `tools/lessons`: the web app as `src/Umsatzschaetzung.Web` hosts it, with a coach over it.
Its services run in their worker as in the app, reader, tagger and ranking included, over the
stores `zustand` kept, which are laid into memory instead of the browser's storage, so every
visit starts the lesson afresh. The weights and the warmed embeddings are fetched as the app
fetches them, the first time they are needed, and kept by the browser for every lesson.

It runs `lesson.json` beat by beat: each beat speaks its clip and moves on when it ends,
cards cover the program, `show` puts a spotlight on the live control, and one small card
says who speaks, which chapter this is and what to do, with a slider along its bottom that
follows the lesson and can be dragged either way at any time. Dragged past a beat the
learner acts in, the coach acts in their place, as the headless program would; dragged
back over one, the program is put back where it stood before that beat began. The ring around
the speaker's face follows the voice while it talks. Subtitles can be switched on there
and stay on for the next visit; the card and subtitles keep clear of what is pointed at.

A `click` or `type` beat waits: only presses and keys on its `allow` targets reach the
program, the target is ringed once the sentence is spoken, a miss shows the `hint` and a
second one rings at once, and the lesson goes on by itself once the program shows `done`
after the learner acted. Only the end says well done. The coach knows nothing about a
lesson, so a new one is a new script and nothing else.
