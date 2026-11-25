-- v3 R1b: the floating windows of the planetarium UI, one row per window (spec §3.3).
-- arrangement: 'Floating', 'Maximized', 'DockLeft' or 'DockRight'. x, y, width, height: the floating
-- rectangle in CSS pixels (kept while maximised or docked); dock_width: the width as a side sheet.
CREATE TABLE window_layouts (
    window_id    TEXT PRIMARY KEY,
    is_open      INTEGER NOT NULL,
    is_minimized INTEGER NOT NULL,
    arrangement  TEXT NOT NULL,
    x            INTEGER NOT NULL,
    y            INTEGER NOT NULL,
    width        INTEGER NOT NULL,
    height       INTEGER NOT NULL,
    dock_width   INTEGER NOT NULL,
    z_order      INTEGER NOT NULL
);
