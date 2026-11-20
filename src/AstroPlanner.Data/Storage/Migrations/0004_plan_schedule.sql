-- M3b: a plan item the user locked keeps its block (local wall-clock, ISO 8601 without offset).
ALTER TABLE plan_items ADD COLUMN locked INTEGER NOT NULL DEFAULT 0;
ALTER TABLE plan_items ADD COLUMN start_local TEXT;
ALTER TABLE plan_items ADD COLUMN end_local TEXT;
