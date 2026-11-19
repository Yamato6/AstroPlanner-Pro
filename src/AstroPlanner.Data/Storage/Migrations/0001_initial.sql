-- AstroPlanner Pro user database, version 1 (M1).

-- Settings as key → JSON value (the observer settings are one document under 'observer').
CREATE TABLE settings (
    key   TEXT PRIMARY KEY NOT NULL,
    value TEXT NOT NULL
);

-- Saved observing sites. horizon_mask: JSON array of {"az": degrees, "alt": degrees}.
CREATE TABLE locations (
    id                   TEXT PRIMARY KEY NOT NULL,
    position             INTEGER NOT NULL,
    name                 TEXT NOT NULL,
    latitude_degrees     REAL NOT NULL,
    longitude_degrees    REAL NOT NULL,
    elevation_meters     REAL NOT NULL,
    iana_time_zone_id    TEXT NOT NULL,
    windows_time_zone_id TEXT NOT NULL,
    horizon_mask         TEXT NOT NULL DEFAULT '[]',
    notes                TEXT NOT NULL DEFAULT ''
);

-- "My plan", in the user's order.
CREATE TABLE plan_items (
    position  INTEGER PRIMARY KEY NOT NULL,
    object_id TEXT NOT NULL,
    note      TEXT NOT NULL DEFAULT ''
);

-- Observation logbook. observed_at_local: wall-clock time, ISO 8601 without offset.
CREATE TABLE logbook (
    id                TEXT PRIMARY KEY NOT NULL,
    object_id         TEXT NOT NULL,
    object_name       TEXT NOT NULL,
    observed_at_local TEXT NOT NULL,
    location          TEXT NOT NULL,
    equipment         TEXT NOT NULL,
    seeing            INTEGER NOT NULL,
    transparency      INTEGER NOT NULL,
    rating            INTEGER NOT NULL,
    notes             TEXT NOT NULL
);

CREATE INDEX logbook_object ON logbook (object_id);
