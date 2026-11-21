import { describe, expect, it } from 'vitest';
import { cartTotals, type Cart, type CartLine } from './cart';

const line = (quantity: number, unitPrice: number, key: string): CartLine => ({
  key, productId: Number(key), productUnitId: null, factor: 1, stock: 10,
  base: { productUnitId: null, unitName: 'كيلو', factor: 1, retailPrice: unitPrice, wholesalePrice: unitPrice, minSalePrice: 0 } as CartLine['base'],
  units: [], name: 'x', sku: key, unitName: 'كيلو', imageFileId: null, available: 10,
  retailPrice: unitPrice, wholesalePrice: unitPrice, minSalePrice: 0, cost: null, quantity, unitPrice, customPrice: false, lineDiscount: 0,
});

describe('cart totals', () => {
  it('rounds each line as the server does, then adds them up', () => {
    // 0.5 × 1.01 = 0.505 each: the server counts 0.51 + 0.51 = 1.02 (adding up first would give 1.01, an agora short).
    const cart: Cart = { key: 'k', tier: 'Retail', lines: [line(0.5, 1.01, '1'), line(0.5, 1.01, '2')] };
    expect(cartTotals(cart).subtotal).toBe(1.02);
    expect(cartTotals(cart).gross).toBe(1.02);
  });
});
