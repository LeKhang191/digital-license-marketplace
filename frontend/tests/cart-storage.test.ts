import { test } from "node:test";
import assert from "node:assert/strict";
import { activateScope, customerScope, GUEST_SCOPE, mergeItems, readStore } from "../src/cart/cartStorage.ts";
const item = (productId: string, quantity: number) => ({ productId, quantity, name: productId, price: 100 });
const a = customerScope("user-a"), b = customerScope("user-b");
test("separate guest and customer carts", () => {
  const store = readStore(JSON.stringify({ [a]: [item("a", 9)], [b]: [item("b", 2)] }));
  assert.equal(store[GUEST_SCOPE], undefined);
  assert.equal(store[a][0].quantity, 9);
  assert.equal(store[b][0].quantity, 2);
});
test("merge guest once, preserve other accounts, cap quantities", () => {
  const store = { [a]: [item("a", 9)], [b]: [item("b", 2)], [GUEST_SCOPE]: [item("a", 3), item("c", 1)] };
  const merged = activateScope(store, a);
  assert.deepEqual(merged[a], [ { ...item("a", 10), image: undefined }, { ...item("c", 1), image: undefined } ]);
  assert.deepEqual(merged[b], store[b]);
  assert.deepEqual(merged[GUEST_SCOPE], []);
  assert.equal(activateScope(merged, a), merged);
});
test("guest activation does not expose customer cart", () => {
  const store = { [a]: [item("a", 9)] };
  assert.equal(activateScope(store, GUEST_SCOPE), store);
});
test("invalid storage and legacy shape rejected", () => {
  assert.equal(Object.keys(readStore("broken")).length, 0);
  assert.equal(Object.keys(readStore(JSON.stringify([item("a", 9)]))).length, 0);
});
test("duplicate products are combined and capped", () => {
  assert.equal(mergeItems([item("a", 8)], [item("a", 4)])[0].quantity, 10);
});
