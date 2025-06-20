![Banner](Images/Always_in_focus.png)

# Always in Focus

A lightweight utility designed to solve a very specific—but surprisingly disruptive—issue: when using PowerPoint in Presenter Mode, embedded videos would sometimes **pause or stutter** if the app lost focus. It wasn’t a critical error, more of a **presentation nitpick**… but it interrupted flow and polish at precisely the wrong time.

> **“Always in Focus” keeps your selected application—like PowerPoint—front and center at all times.**

---

## 🖼 Main Dashboard

![Main Panel UI](Images/image-main.png) <!-- Replace with actual filename -->

The main dashboard is your command center. It lists saved focus rules—like a preset for PowerPoint—and allows you to edit or delete them as needed. Clean interface, quick access.

---

## 🛠 Rule Editor

![Rule Editor](Images/image-editor.png) <!-- Replace with actual filename -->

Each rule includes:
- A **custom label** (like “PowerPoint Presenting”),
- A **unique ID**, possibly matching the window title or process name.

This allows precise targeting—even across different workflows or apps.

---

## 🧠 How It Works

The system persists everything in a **lightweight CSV configuration**, including:
- The last selected app (e.g. `PowerPoint`),
- The ON/OFF toggle state,
- Any rule settings you've created.

This means:
- On launch, the app **restores your last-used state** immediately.
- If PowerPoint was set and the toggle was ON, it jumps right back into action.

---

## 📎 System Tray Controls

![Tray Menu](Images/image-tray.png) <!-- Replace with actual filename -->

The app runs quietly in the background. From the tray, you can:
- **Show** the main interface
- **Turn On/Off** the focus enforcement
- **Exit** the app

---

## ⚠ Known Quirk

There is a minor side effect worth noting: when forcing PowerPoint to retain focus during video playback, you may occasionally notice a small **microjitter** or visual hiccup. It’s rare and often negligible, but worth keeping in mind for presentations that rely on **frame-perfect video transitions**.

---

This is one of those “quality-of-life” tools—small in size, huge in impact. If you’ve ever dealt with PowerPoint behaving unpredictably mid-talk, **Always in Focus** might just be your secret weapon.

