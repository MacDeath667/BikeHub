import { test } from "node:test";
import assert from "node:assert/strict";
import { rideInstant, rideLocalTime } from "../src/components/rideTime";

test("ride time follows the meeting zone rather than the viewer device", () => {
  assert.equal(rideInstant("2026-12-01T10:00", "Europe/Moscow"), "2026-12-01T07:00:00.000Z");
  assert.equal(rideLocalTime("2026-12-01T07:00:00Z", "Europe/Moscow"), "2026-12-01T10:00");
  assert.equal(rideInstant("2026-12-01T00:15", "Asia/Kolkata"), "2026-11-30T18:45:00.000Z");
  assert.equal(rideInstant("2026-12-01T10:00", "UTC"), "2026-12-01T10:00:00.000Z");
});
test("nonexistent and ambiguous daylight-saving times are rejected explicitly", () => {
  assert.throws(() => rideInstant("2027-03-14T02:30", "America/New_York"), /такого времени нет/);
  assert.throws(() => rideInstant("2027-11-07T01:30", "America/New_York"), /повторяется/);
  assert.throws(() => rideInstant("not a date", "UTC"), /дату/);
  assert.throws(() => rideInstant("2027-02-31T12:00", "UTC"), /такого времени нет/);
  assert.throws(() => rideInstant("2027-01-01T12:00", "Missing/Zone"));
});
