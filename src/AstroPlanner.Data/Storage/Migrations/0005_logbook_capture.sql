-- M6b: photographed logbook entries (spec §4.7) and the model's prediction kept with them (§8.7).
-- kind: 'Observed' or 'Photographed'. image_path: a file the user chose (never copied).
-- prediction: JSON {"verdict", "score", "snr", "subSeconds", "subCount"}, null when there was no verdict.
ALTER TABLE logbook ADD COLUMN kind TEXT NOT NULL DEFAULT 'Observed';
ALTER TABLE logbook ADD COLUMN rig_id TEXT;
ALTER TABLE logbook ADD COLUMN rig_name TEXT;
ALTER TABLE logbook ADD COLUMN sub_seconds REAL;
ALTER TABLE logbook ADD COLUMN iso INTEGER;
ALTER TABLE logbook ADD COLUMN gain INTEGER;
ALTER TABLE logbook ADD COLUMN sub_count INTEGER;
ALTER TABLE logbook ADD COLUMN image_path TEXT;
ALTER TABLE logbook ADD COLUMN prediction TEXT;
