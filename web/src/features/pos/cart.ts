import { useCallback, useEffect, useState } from 'react';
import type { ProductDetail, ProductListItem } from '@/api/catalog';
import type { PriceTier } from '@/api/sales';
import { round2 } from '@/lib/numbers';

export interface CartLine {
  productId: number;
  name: string;
  sku: string;
  unitName: string;
  imageFileId: string | null;
  available: number;
  retailPrice: number;
  wholesalePrice: number;
  minSalePrice: number;
  /** Average cost when the viewer may see it; used only for warnings. */
  cost: number | null;
  quantity: number;
  unitPrice: number;
  /** Price typed by hand; switching retail/wholesale leaves it alone. */
  customPrice: boolean;
  lineDiscount: number;
}

export interface Cart {
  /** Idempotency key of this sale: the same key on every retry, a new one after success. */
  key: string;
  tier: PriceTier;
  lines: CartLine[];
}

const STORAGE_KEY = 'qwader.pos.cart.v1';
const newCart = (tier: PriceTier = 'Retail'): Cart => ({ key: crypto.randomUUID(), tier, lines: [] });

function load(): Cart {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) {
      const parsed = JSON.parse(raw) as Cart;
      if (parsed?.key && Array.isArray(parsed.lines)) return parsed;
    }
  } catch {
    /* storage unavailable or corrupt: start empty */
  }
  return newCart();
}

export const tierPrice = (l: Pick<CartLine, 'retailPrice' | 'wholesalePrice'>, tier: PriceTier) => (tier === 'Wholesale' ? l.wholesalePrice : l.retailPrice);

export const lineTotal = (l: CartLine) => round2(l.quantity * l.unitPrice - l.lineDiscount);

export function cartTotals(cart: Cart) {
  const gross = round2(cart.lines.reduce((s, l) => s + l.quantity * l.unitPrice, 0));
  const lineDiscounts = round2(cart.lines.reduce((s, l) => s + l.lineDiscount, 0));
  const listTotal = round2(cart.lines.reduce((s, l) => s + l.quantity * tierPrice(l, cart.tier), 0));
  const items = cart.lines.reduce((s, l) => s + l.quantity, 0);
  return { gross, lineDiscounts, subtotal: round2(gross - lineDiscounts), listTotal, items };
}

type ProductLike = Pick<ProductListItem, 'id' | 'displayName' | 'sku' | 'unitName' | 'imageFileId' | 'quantity' | 'retailPrice' | 'wholesalePrice' | 'minSalePrice' | 'averageCost'>;

export function toCartProduct(p: ProductDetail): ProductLike {
  return { ...p, averageCost: p.averageCost };
}

/** Cart state kept in localStorage, so a reload or a dropped connection never loses a sale in progress. */
export function useCart() {
  const [cart, setCart] = useState<Cart>(load);

  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(cart));
    } catch {
      /* ignore: the cart still works in memory */
    }
  }, [cart]);

  /** Adds one unit. Returns an Arabic message when stock does not allow it. */
  const add = useCallback((p: ProductLike, quantity = 1): string | null => {
    let error: string | null = null;
    setCart((c) => {
      const existing = c.lines.find((l) => l.productId === p.id);
      const wanted = (existing?.quantity ?? 0) + quantity;
      if (wanted > p.quantity) {
        error = p.quantity <= 0 ? `«${p.displayName}» غير متوفر في المخزون` : `المتوفر من «${p.displayName}» هو ${p.quantity} فقط`;
        return c;
      }
      if (existing) return { ...c, lines: c.lines.map((l) => (l === existing ? { ...l, quantity: wanted, available: p.quantity } : l)) };
      const line: CartLine = {
        productId: p.id,
        name: p.displayName,
        sku: p.sku,
        unitName: p.unitName,
        imageFileId: p.imageFileId,
        available: p.quantity,
        retailPrice: p.retailPrice,
        wholesalePrice: p.wholesalePrice,
        minSalePrice: p.minSalePrice,
        cost: p.averageCost,
        quantity,
        unitPrice: c.tier === 'Wholesale' ? p.wholesalePrice : p.retailPrice,
        customPrice: false,
        lineDiscount: 0,
      };
      return { ...c, lines: [line, ...c.lines] };
    });
    return error;
  }, []);

  const update = useCallback((productId: number, patch: Partial<CartLine>) => {
    setCart((c) => ({ ...c, lines: c.lines.map((l) => (l.productId === productId ? { ...l, ...patch } : l)) }));
  }, []);

  const remove = useCallback((productId: number) => setCart((c) => ({ ...c, lines: c.lines.filter((l) => l.productId !== productId) })), []);

  const setTier = useCallback(
    (tier: PriceTier) =>
      setCart((c) => ({ ...c, tier, lines: c.lines.map((l) => (l.customPrice ? l : { ...l, unitPrice: tierPrice(l, tier) })) })),
    [],
  );

  const reset = useCallback(() => setCart((c) => newCart(c.tier)), []);

  return { cart, add, update, remove, setTier, reset };
}
