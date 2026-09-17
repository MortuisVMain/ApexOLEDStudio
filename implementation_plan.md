# Studio Redesign: Pro 3-Column Workspace (Figma & L-Connect 3 Architecture)

## Problem Identified from User Screenshot & Feedback
1. **Critical Usability Blocker ("виджеты поменять не возможно у тебя кнопки закрывают место с выбором виджетов")**:
   - The root window grid had 4 stacked rows:
     - Row 0: Top Bar
     - Row 1: Virtual OLED display preview (~320px tall)
     - Row 2: Tabs with `Height="*"` (only ~350px left!)
     - Row 3: Footer
   - In Row 2, Column 0 attempted to stack the header, 12 preset buttons, action buttons, the `ListBox`, and batch controls all vertically.
   - The `ListBox` was choked down to an unusable 30px sliver, making it impossible to see or click widgets.
2. **"Нейрослоп" Layout vs Professional Studio UX**:
   - Top-tier software (Lian Li L-Connect 3, NZXT CAM, Figma, SteelSeries GG) never stacks a giant preview on top of a 3-column crushed tab set.
   - Modern design tools use a **unified 3-column Studio workspace**:
     - **Left Column**: Full-height **WIDGET LAYERS** tree (650px+ vertical height, spacious, easy to select, toggle, and reorder).
     - **Center Column**: **STUDIO OLED CANVAS** with interactive 768x240 preview, canvas toolbar, and a **1-Click Quick Widget Dock** directly underneath the screen.
     - **Right Column**: **WIDGET INSPECTOR & LIVE TELEMETRY** (full-height property inspector with typography, metrics, and live hardware cards).

---

## The Critic Triad Audit (`critic-triad`)

```mermaid
flowchart TD
    subgraph TriadCouncil["⚔️ Коллегия Трёх Критиков"]
        C1["🔴 Скептик (Red-Team)<br/>- Риск: Перенос Canvas в Center Column не должен сломать интерактивное выделение и драг мышью.<br/>- Риск: На экранах с низким разрешением (1366x768) холст 768px может переполнить центр.<br/>- Решение: Viewbox / MaxWidth с сохранением четкости NearestNeighbor."]
        C2["🟢 Прагматик (Карпати 200->50)<br/>- Убрать лишний уровень вложенности: вместо вложенных Grid и TabControl сделать цельный монолитный Studio Layout.<br/>- Полноразмерный ListBox виджетов слева решает проблему некликабельности навсегда."]
        C3["🔵 Профильный Эксперт (open-design-pro / L-Connect 3)<br/>- Референс: Lian Li L-Connect 3 & Figma.<br/>- Слева: дерево слоев виджетов.<br/>- По центру: холст экрана + горизонтальный док виджетов под ним.<br/>- Справа: инспектор свойств и мониторинг."]
    end
```

---

## Proposed Architecture: Unified 3-Column Studio Workspace

```mermaid
flowchart LR
    subgraph Window["APEX OLED STUDIO (Window)"]
        TopBar["Top Bar (Header: Logo + Status + Presets + Send to OLED)"]
        subgraph StudioLayout["Main Studio Workspace (Height: *)"]
            LeftPanel["LEFT: Widget Layers<br/>- Full Height (650px)<br/>- Spacious cards with Eye toggle<br/>- Action buttons: Custom, Copy, Delete"]
            CenterPanel["CENTER: Studio Canvas & Dock<br/>- Canvas Bezel (768x240)<br/>- Interactive Selection<br/>- Quick 1-Click Widget Dock"]
            RightPanel["RIGHT: Inspector & Telemetry<br/>- Tab 1: Widget Inspector (X, Y, W, H, Tokens)<br/>- Tab 2: Live Hardware Telemetry"]
        end
        Footer["Footer Bar (Burn-in Status + HID Engine)"]
    end
```

### Key Changes in `MainWindow.xaml`:
1. **Eliminate Row 1 OLED Billboard**:
   - Move the Virtual OLED display preview from the stacked top row directly into the **Center Column** of the main workspace.
2. **Full-Height Widget Layers List (Left Column, Width 300px)**:
   - Entire column dedicated to the active widgets list.
   - Clean layer cards: Eye icon (Visibility toggle), Widget Type badge, Name, Metric key, Coordinates chip.
   - No buttons squashing the list!
3. **Quick-Add Widget Dock (Under Center Canvas)**:
   - Position the 12 quick-add preset buttons in a sleek horizontal tool dock right below the OLED screen.
   - 1 click on `[⚡ Watts]`, `[🌡 Temps]`, `[▓ CPU]`, etc., drops the widget onto the canvas and selects it in the layer list.
4. **Right Column (Inspector & Telemetry, Width 340px)**:
   - Full height property inspector with live hardware telemetry.
