-- Equipment inventory (spec §4.5): cameras, optics, mounts and rigs. The active rig and the
-- "demo profile seeded" flag live in the settings table.
CREATE TABLE cameras (
    id                    TEXT PRIMARY KEY,
    position              INTEGER NOT NULL,
    name                  TEXT NOT NULL,
    kind                  TEXT NOT NULL,
    color                 TEXT NOT NULL,
    sensor_width_mm       REAL NOT NULL,
    sensor_height_mm      REAL NOT NULL,
    resolution_width_px   INTEGER NOT NULL,
    resolution_height_px  INTEGER NOT NULL,
    pixel_size_um         REAL,
    quantum_efficiency    REAL NOT NULL,
    read_noise_e          REAL NOT NULL,
    dark_current_e_per_s  REAL NOT NULL,
    recommended_iso       INTEGER,
    recommended_gain      INTEGER,
    notes                 TEXT NOT NULL DEFAULT ''
);

CREATE TABLE optics (
    id               TEXT PRIMARY KEY,
    position         INTEGER NOT NULL,
    name             TEXT NOT NULL,
    kind             TEXT NOT NULL,
    focal_length_mm  REAL NOT NULL,
    aperture_mm      REAL NOT NULL,
    transmission     REAL NOT NULL,
    notes            TEXT NOT NULL DEFAULT ''
);

CREATE TABLE mounts (
    id                        TEXT PRIMARY KEY,
    position                  INTEGER NOT NULL,
    name                      TEXT NOT NULL,
    kind                      TEXT NOT NULL,
    max_unguided_exposure_s   REAL,
    payload_kg                REAL,
    notes                     TEXT NOT NULL DEFAULT ''
);

CREATE TABLE rigs (
    id                TEXT PRIMARY KEY,
    position          INTEGER NOT NULL,
    name              TEXT NOT NULL,
    camera_id         TEXT NOT NULL REFERENCES cameras (id),
    optic_id          TEXT NOT NULL REFERENCES optics (id),
    mount_id          TEXT NOT NULL REFERENCES mounts (id),
    focal_multiplier  REAL NOT NULL DEFAULT 1,
    notes             TEXT NOT NULL DEFAULT ''
);
