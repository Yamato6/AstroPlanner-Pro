-- M2: measured zenith sky brightness (SQM, mag/arcsec²) of a saved site; NULL = unknown.
ALTER TABLE locations ADD COLUMN zenith_sqm REAL;
