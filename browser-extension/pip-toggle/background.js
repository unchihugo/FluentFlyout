// Toggles picture-in-picture for the video that is playing in this browser.
//
// Two ways in: the global shortcut Ctrl+Shift+6 (works while the browser is in the background, which is what
// FluentFlyout sends) and a click on the toolbar button.
//
// IMPORTANT: every function that is handed to chrome.scripting.executeScript is serialised on its own and runs
// inside the page. It cannot call any other function of this file - doing so throws a ReferenceError inside the
// page, the injection still looks like it succeeded and returns nothing at all. Each of those functions therefore
// repeats the "find the video of this frame" logic instead of sharing a helper.
//
// Chromium only lets a video enter picture-in-picture right after a user gesture, and an injected script cannot
// provide one. Chrome 134+ therefore offers the way that needs no gesture: if the page registers a media session
// action handler for "enterpictureinpicture", the browser opens the corner window by itself as soon as the user
// switches away from the tab (and closes it again when the tab becomes visible). This extension registers that
// handler before it tries the direct call.

const COMMAND = "toggle-pip";
const BADGE_MS = 4000;
const BADGE_COLOR = "#0F6CBD";
const BADGE_ARMED_COLOR = "#8A6D00";
const BADGE_ERROR_COLOR = "#B00020";
const BADGE_IDLE_COLOR = "#5F6368";

chrome.commands.onCommand.addListener((command) => {
    if (command === COMMAND) {
        togglePictureInPicture("shortcut " + COMMAND);
    }
});

chrome.action.onClicked.addListener(() => {
    togglePictureInPicture("toolbar button");
});

chrome.runtime.onInstalled.addListener(async (details) => {
    // show the version on the badge for a moment: the quickest way to tell whether a reload really picked up
    // the files that are on disk right now
    try {
        const version = chrome.runtime.getManifest().version;
        await chrome.action.setBadgeBackgroundColor({ color: BADGE_COLOR });
        await chrome.action.setBadgeText({ text: version.split(".").slice(0, 2).join(".") });
        await chrome.action.setTitle({ title: `PiP Toggle ${version} loaded (${details.reason})` });
        setTimeout(() => chrome.action.setBadgeText({ text: "" }), 12000);
        console.log(`[pip-toggle] version ${version} loaded (${details.reason})`);
    } catch (error) {
        console.warn("[pip-toggle] could not show the version", error);
    }

    try {
        for (const command of await chrome.commands.getAll()) {
            console.log(`[pip-toggle] ${command.name} = ${command.shortcut || "(unbound)"}`);
        }
    } catch (error) {
        console.warn("[pip-toggle] could not read the commands", error);
    }
});

async function togglePictureInPicture(source = "unknown") {
    const frames = await probeFrames();

    await remember({ at: new Date().toISOString(), source, frames: frames.length,
        withVideo: frames.filter((frame) => frame.video).length,
        inPip: frames.filter((frame) => frame.pip).length });

    // leaving picture-in-picture wins, so a toggle never closes one view and opens another one right away
    const inPip = frames.filter((frame) => frame.pip);
    if (inPip.length > 0) {
        const left = await runInFrames(inPip, exitPictureInPicture);
        await runInFrames(inPip, disarmAutoPictureInPicture, "MAIN");

        await report(left.length > 0 ? "exit" : "none",
            left.length > 0 ? undefined : "the browser did not leave picture-in-picture", source);
        await remember({ at: new Date().toISOString(), source, result: left.length > 0 ? "exit" : "exit-failed" });
        return;
    }

    const withVideo = frames.filter((frame) => frame.video);

    // the video that is actually running is the one this click is about: a paused video in the tab the user
    // happens to be looking at must never win over the one playing in the background
    withVideo.sort((a, b) => (Number(b.playing) - Number(a.playing)) || (Number(b.audible) - Number(a.audible)));

    const target = withVideo.find((frame) => frame.playing) ?? withVideo[0];

    if (withVideo.length === 0) {
        // nothing to do: this page is not playing a video (a song, a paused player, ...). That is a normal
        // outcome rather than a failure, so it is not reported in red.
        await report("idle", "the page is not playing a video", source);
        await remember({ at: new Date().toISOString(), source, result: "no-video" });
        return;
    }

    // fastest path first: going right away works while the page still has a fresh user gesture, and it needs no
    // automatic entry at all
    const entered = await runInFrames(target ? [target] : [], enterPictureInPicture);
    if (entered.some((result) => result && result.ok)) {
        await report("enter", undefined, source);
        await remember({ at: new Date().toISOString(), source, result: "entered" });
        return;
    }

    // otherwise register the browser's own automatic entry, which needs no user gesture
    const armed = await runInFrames(withVideo, armAutoPictureInPicture, "MAIN");

    if (armed.some((result) => result && result.ok)) {
        const hidden = await isPageHidden(target.tabId);

        // a page that is already in the background cannot go from visible to hidden, and that transition is
        // exactly what the browser's own entry waits for - so there is nothing to wait for here either
        if (!hidden && await waitForPictureInPicture(withVideo, 3)) {
            await report("enter", "the browser opened it by itself", source);
            await remember({ at: new Date().toISOString(), source, result: "entered-auto" });
            return;
        }

        // push the page through that transition: a quick switch to the video's tab and straight back to the tab
        // the user was on. With the browser in the background nobody sees that; in the foreground it is a very
        // short tab switch.
        if (hidden && await forceVisibilityChange([target])) {
            if (await waitForPictureInPicture(withVideo)) {
                await report("enter", "the browser opened the corner window after a quick tab switch", source);
                await remember({ at: new Date().toISOString(), source, result: "entered-after-tab-switch" });
                return;
            }
        }

        await report("armed",
            "switch to another tab or window and the browser opens the picture-in-picture window by itself",
            source);
        await remember({ at: new Date().toISOString(), source, result: "armed",
            enterError: (entered.find((result) => result && result.error) || {}).error,
            armError: (armed.find((result) => result && result.error) || {}).error });
        return;
    }

    const failure = entered.find((result) => result && result.error) ?? armed.find((result) => result && result.error);
    await report("none", failure ? failure.error : "the video refused to enter picture-in-picture", source);
    await remember({ at: new Date().toISOString(), source, result: "failed",
        error: failure ? failure.error : null,
        armError: (armed.find((result) => result && result.error) || {}).error });
}

// looks at every frame of every tab without touching anything: which frame is in picture-in-picture, and which
// frames hold a video that could go there
// whether the tab of the video sits in the background right now
async function isPageHidden(tabId) {
    try {
        const tab = await chrome.tabs.get(tabId);
        return !tab || !tab.active;
    } catch (error) {
        return false;
    }
}

// whether any of the given frames is in picture-in-picture right now
async function waitForPictureInPicture(frames, attempts = 8) {
    for (let attempt = 0; attempt < attempts; attempt++) {
        const results = await runInFrames(frames, probeFrame);
        if (results.some((result) => result && result.pip)) {
            return true;
        }

        await sleep(200);
    }

    return false;
}

// the browser's automatic picture-in-picture waits for the page to go from visible to hidden. A page that is
// already hidden can be pushed into that transition by making it the active tab for a moment and going straight
// back to what the user was looking at.
async function forceVisibilityChange(frames) {
    const frame = frames[0];
    if (!frame) {
        return false;
    }

    try {
        const tab = await chrome.tabs.get(frame.tabId);
        if (!tab || tab.active) {
            return false;
        }

        const active = await chrome.tabs.query({ active: true, windowId: tab.windowId });
        const previous = active && active.length > 0 ? active[0].id : undefined;

        await chrome.tabs.update(tab.id, { active: true });
        await sleep(200);

        if (typeof previous === "number") {
            await chrome.tabs.update(previous, { active: true });
        }

        await remember({ at: new Date().toISOString(), result: "made-visibility-change", tabId: tab.id,
            previous });

        return true;
    } catch (error) {
        await remember({ at: new Date().toISOString(), result: "visibility-change-failed", error: String(error) });
        return false;
    }
}

function sleep(milliseconds) {
    return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

async function probeFrames() {
    const frames = [];
    const failures = [];
    const emptyResults = [];

    for (const tabId of await collectTabIds()) {
        try {
            const injected = await chrome.scripting.executeScript({
                target: { tabId, allFrames: true },
                func: probeFrame,
            });

            if (!injected || injected.length === 0) {
                emptyResults.push({ tabId, why: "no frame was injected" });
                continue;
            }

            for (const frame of injected) {
                if (frame && frame.result) {
                    frames.push({ tabId, frameId: frame.frameId, audible: audibleTabs.has(tabId), ...frame.result });
                } else {
                    // the frame ran the function but it returned nothing: it threw inside the page
                    emptyResults.push({ tabId, frameId: frame && frame.frameId, why: "the injected function returned nothing" });
                }
            }
        } catch (error) {
            failures.push({ tabId, error: String(error) });
        }

        // the sounding tab already has a video: nothing else needs to be looked at
        if (frames.length > 0 && audibleTabs.has(tabId)) {
            break;
        }
    }

    if (failures.length > 0) {
        await remember({ at: new Date().toISOString(), result: "injection-failed", failures: failures.slice(0, 3) });
    }

    if (emptyResults.length > 0) {
        await remember({ at: new Date().toISOString(), result: "injection-empty", samples: emptyResults.slice(0, 4) });
    }

    return frames;
}

const tabUrls = new Map();
const audibleTabs = new Set();

// every tab of the browser, the active one first: that is where the video the user is watching usually lives.
// chrome.tabs.query is used on purpose instead of chrome.windows.getAll({populate: true}), because populate only
// fills in the tabs when the extension also holds the tabs permission and otherwise returns windows without any
// tabs at all.
async function collectTabIds() {
    tabUrls.clear();
    audibleTabs.clear();

    let tabs = [];

    try {
        tabs = (await chrome.tabs.query({})) || [];
    } catch (error) {
        await remember({ at: new Date().toISOString(), result: "tab-query-failed", error: String(error) });
    }

    if (tabs.length === 0) {
        try {
            const windows = await chrome.windows.getAll({ populate: true, windowTypes: ["normal"] });
            const collected = [];

            for (const window of windows) {
                for (const tab of window.tabs || []) {
                    if (typeof tab.id === "number") {
                        collected.push(tab);
                    }
                }
            }

            tabs = collected;
            await remember({ at: new Date().toISOString(), result: "tabs-via-windows", count: tabs.length });
        } catch (error) {
            await remember({ at: new Date().toISOString(), result: "windows-fallback-failed", error: String(error) });
            return [];
        }
    }

    for (const tab of tabs) {
        if (typeof tab.id === "number") {
            tabUrls.set(tab.id, tab.url || "(no url)");
            if (tab.audible) {
                audibleTabs.add(tab.id);
            }
        }
    }

    // the tab that is playing sound is almost always the one with the video, so it is looked at first
    tabs.sort((a, b) => (Number(b.active) - Number(a.active)) || (Number(b.audible) - Number(a.audible)));

    return tabs.map((tab) => tab.id).filter((id) => typeof id === "number");
}

async function runInFrames(frames, func, world) {
    const results = [];

    for (const frame of frames) {
        try {
            const injection = {
                target: { tabId: frame.tabId, frameIds: [frame.frameId] },
                func,
            };

            if (world) {
                injection.world = world;
            }

            const injected = await chrome.scripting.executeScript(injection);

            for (const result of injected || []) {
                if (result && result.result) {
                    results.push(result.result);
                } else {
                    results.push({ ok: false, error: "the injected function returned nothing" });
                }
            }
        } catch (error) {
            results.push({ ok: false, error: String(error) });
        }
    }

    return results;
}

// ---------------------------------------------------------------------------------------------------------------
// Everything below runs inside the page. Each function has to stand on its own: no helper functions, no shared
// constants, because only the function's own source is injected.
// ---------------------------------------------------------------------------------------------------------------

function probeFrame() {
    if (document.pictureInPictureElement) {
        return { pip: true, video: false, playing: false };
    }

    if (!document.pictureInPictureEnabled) {
        return { pip: false, video: false, playing: false };
    }

    const videos = Array.from(document.querySelectorAll("video")).filter(
        (video) => video.readyState > 0 && video.videoWidth > 0 && !video.disablePictureInPicture);

    // a playing video first, a paused one as the fallback (picture-in-picture also works while paused)
    const playing = videos.find((candidate) => !candidate.paused && !candidate.ended) || null;
    const video = playing || videos[0] || null;

    return { pip: false, video: Boolean(video), playing: Boolean(playing) };
}

function enterPictureInPicture() {
    const videos = Array.from(document.querySelectorAll("video")).filter(
        (video) => video.readyState > 0 && video.videoWidth > 0 && !video.disablePictureInPicture);

    const video = videos.find((candidate) => !candidate.paused && !candidate.ended) || videos[0] || null;

    if (!video) {
        return { ok: false, error: "this frame has no usable video" };
    }

    return video
        .requestPictureInPicture()
        .then(() => ({ ok: true, action: "enter" }))
        .catch((error) => ({
            ok: false,
            action: "enter",
            error: error && error.name === "NotAllowedError"
                ? "NotAllowedError - the browser wants a fresh user gesture"
                : (error && error.name ? error.name + ": " + error.message : String(error)),
        }));
}

function exitPictureInPicture() {
    if (!document.pictureInPictureElement) {
        return null;
    }

    return document
        .exitPictureInPicture()
        .then(() => ({ ok: true, action: "exit" }))
        .catch((error) => ({ ok: false, action: "exit", error: String(error) }));
}

function armAutoPictureInPicture() {
    if (!("mediaSession" in navigator) || !navigator.mediaSession) {
        return { ok: false, error: "this page has no media session" };
    }

    const videos = Array.from(document.querySelectorAll("video")).filter(
        (video) => video.readyState > 0 && video.videoWidth > 0 && !video.disablePictureInPicture);

    const video = videos.find((candidate) => !candidate.paused && !candidate.ended) || videos[0] || null;

    if (!video) {
        return { ok: false, error: "this frame has no usable video" };
    }

    try {
        // from Chrome 134 the browser calls this handler by itself when the user switches away from the tab, and
        // it does so without needing a user gesture
        navigator.mediaSession.setActionHandler("enterpictureinpicture", async () => {
            try {
                await video.requestPictureInPicture();
            } catch (error) {
                console.warn("[pip-toggle] the browser asked for picture-in-picture and it failed", error);
            }
        });

        return { ok: true, action: "armed" };
    } catch (error) {
        return { ok: false, action: "armed", error: String(error) };
    }
}

function disarmAutoPictureInPicture() {
    try {
        if ("mediaSession" in navigator && navigator.mediaSession) {
            navigator.mediaSession.setActionHandler("enterpictureinpicture", null);
        }

        return { ok: true, action: "disarmed" };
    } catch (error) {
        return { ok: false, action: "disarmed", error: String(error) };
    }
}

// ---------------------------------------------------------------------------------------------------------------

// keeps the last handful of runs, readable through chrome.storage.local (and by reading the profile on disk)
async function remember(entry) {
    try {
        const stored = await chrome.storage.local.get("pipLog");
        const log = Array.isArray(stored.pipLog) ? stored.pipLog : [];
        log.push(entry);
        while (log.length > 25) {
            log.shift();
        }
        await chrome.storage.local.set({ pipLog: log });
    } catch (error) {
        // diagnostics only
    }
}

// the badge and the tooltip say what happened, so a failing click can be explained without the console
async function report(action, detail, source) {
    const text = action === "enter" ? "PiP" : action === "exit" ? "off" : action === "armed" ? "arm"
        : action === "idle" ? "-" : "!";
    const title = action === "enter"
        ? "PiP Toggle — entered picture-in-picture"
        : action === "exit"
            ? "PiP Toggle — left picture-in-picture"
            : action === "armed"
                ? `PiP Toggle — ready: ${detail ?? "switch away from the tab"}`
                : action === "idle"
                    ? `PiP Toggle — nothing to do: ${detail ?? "not a video"}`
                    : `PiP Toggle — did nothing: ${detail ?? "unknown reason"}`;

    console.log(`[pip-toggle] ${action} via ${source ?? "unknown"}${detail ? ` (${detail})` : ""}`);

    try {
        await chrome.action.setTitle({ title: `${title} (${source ?? "unknown"})` });
        await chrome.action.setBadgeBackgroundColor({
            color: action === "none" ? BADGE_ERROR_COLOR
                : action === "armed" ? BADGE_ARMED_COLOR
                    : action === "idle" ? BADGE_IDLE_COLOR
                        : BADGE_COLOR,
        });
        await chrome.action.setBadgeText({ text });

        // "arm" stays visible: it means the browser will open the corner window by itself, and that is easy to
        // mistake for nothing having happened
        if (action !== "armed") {
            setTimeout(() => chrome.action.setBadgeText({ text: "" }), BADGE_MS);
        }
    } catch (error) {
        // the badge is cosmetic only
    }
}
