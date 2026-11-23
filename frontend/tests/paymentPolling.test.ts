import test from "node:test";
import assert from "node:assert/strict";
import {
  nextPollDelayMs,
  isFinalStatus,
  POLL_INTERVAL_MS,
  MAX_POLL_ATTEMPTS,
} from "../src/pages/paymentPolling.ts";

test("trạng thái cuối thì dừng poll", () => {
  for (const s of ["COMPLETED", "REFUNDED", "CANCELED"] as const) {
    assert.equal(isFinalStatus(s), true);
    assert.equal(nextPollDelayMs(s, 1), null);
  }
});

test("PENDING / PAID / REFUNDING tiếp tục poll theo chu kỳ", () => {
  for (const s of ["PENDING", "PAID", "REFUNDING"] as const) {
    assert.equal(isFinalStatus(s), false);
    assert.equal(nextPollDelayMs(s, 1), POLL_INTERVAL_MS);
  }
});

test("đơn kẹt ở PAID không poll vô hạn: dừng sau MAX_POLL_ATTEMPTS", () => {
  assert.equal(nextPollDelayMs("PAID", MAX_POLL_ATTEMPTS - 1), POLL_INTERVAL_MS);
  assert.equal(nextPollDelayMs("PAID", MAX_POLL_ATTEMPTS), null);
  assert.equal(nextPollDelayMs("PAID", MAX_POLL_ATTEMPTS + 100), null);
});

test("tổng thời gian poll tối đa là 60 giây", () => {
  assert.equal(POLL_INTERVAL_MS * MAX_POLL_ATTEMPTS, 60_000);
});
