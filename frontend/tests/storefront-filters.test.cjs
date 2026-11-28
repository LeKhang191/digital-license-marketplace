// Run from frontend after npm ci:
// npx --no-install tsc src/pages/storefrontFilters.ts --outDir /tmp/dlm-storefront-tests --module commonjs --target es2022 --skipLibCheck && node tests/storefront-filters.test.cjs /tmp/dlm-storefront-tests/pages/storefrontFilters.js
const assert = require('node:assert/strict');
const { filterProducts, pageNumbers, readPrice, PAGE_SIZE } = require(process.argv[2]);
const products = Array.from({length: 59}, (_, i) => ({
  id: String(i), name: `Game ${String(i).padStart(2, '0')}`, description: 'Demo',
  category: i === 0 ? 'Software' : 'Game', price: (59-i)*1000,
  isActive: true, createdAtUtc: new Date(Date.UTC(2026, 0, i+1)).toISOString(),
  steam: {genres: [i%2 ? 'Action' : 'Strategy']}
}));
const f = (overrides = {}) => filterProducts(products, {search:'',category:'',genre:'',sort:'default',...overrides});
assert.equal(f().length,59);
assert.equal(f({sort:'price-asc'})[0].id,'58');
assert.equal(f({sort:'price-desc'})[0].id,'0');
assert.equal(f({sort:'name-asc'})[0].id,'0');
assert.equal(f({sort:'name-desc'})[0].id,'58');
assert.equal(f({sort:'newest'})[0].id,'58');
assert.equal(f({category:'Software'}).length,1);
assert.equal(f({genre:'action'}).length,29);
assert.deepEqual(f({min:1000,max:3000}).map(p=>p.price),[3000,2000,1000]);
assert.equal(f({search:'GAME 58'}).length,1);
assert.equal(f({category:'Game',genre:'Action',min:1000,max:3000}).length,1);
assert.equal(f().slice(PAGE_SIZE*4).length,11);
assert.deepEqual(pageNumbers(1,5),[1,2,3,'gap-5',5]);
assert.deepEqual(pageNumbers(3,5),[1,2,3,4,5]);
assert.deepEqual(pageNumbers(1,1),[1]);
assert.equal(readPrice('0'),0);
assert.equal(readPrice('-5'),undefined);
assert.equal(readPrice('Infinity'),undefined);
assert.equal(readPrice(''),undefined);
products[0].isActive=false;
assert.equal(f().length,58);
console.log('PASS: 20 storefront filter and pagination assertions');
