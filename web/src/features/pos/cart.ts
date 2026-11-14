import { useCallback, useEffect, useState } from 'react';
import type { ProductDetail, ProductListItem, ProductUnit } from '@/api/catalog';
import type { PriceTier } from '@/api/sales';
import { round2 } from '@/lib/numbers';

export interface CartLine {
  /** productId:unit — the same product may be on the invoice by the piece and by the dozen. */
  key: string;
  productId: number;
  /** Extra unit sold in (dozen); null = the product's base unit. */
  productUnitId: number | null;
  /** Base units in one of this line's unit (1 for the base unit). */
  factor: number;
  /** The product's stock in base units. */
  stock: number;
  /** What the product can be sold in, to switch the line's unit. */
  base: UnitChoice;
  units: ProductUnit[];
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

/** The prices of one way of selling a product (its base unit or an extra unit). */
export interface UnitChoice {
  productUnitId: number | null;
  unitName: string;
  factor: number;
  retailPrice: number;
  wholesalePrice: number;
  minSalePrice: number;
}

export const lineKey = (productId: number, productUnitId: number | null) => `${productId}:${productUnitId ?? 0}`;

export const unitChoices = (l: Pick<CartLine, 'base' | 'units'>): UnitChoice[] => [
  l.base,
  ...l.units.map((u) => ({ productUnitId: u.id, unitName: u.unitName, factor: u.factor, retailPrice: u.retailPrice, wholesalePrice: u.wholesalePrice, minSalePrice: u.minSalePrice })),
];

/** How much of each line can still be sold: the product's stock less what its other lines take, in the line's unit. */
function withAvailability(lines: CartLine[]): CartLine[] {
  return lines.map((l) => {
    const others = lines.filter((o) => o.productId === l.productId && o.key !== l.key).reduce((s, o) => s + o.quantity * o.factor, 0);
    // A bigger unit counts whole packs (28 pieces = 2 dozens, not 2.333); the base unit keeps fractions (kg, metres).
    const fits = (l.stock - others) / l.factor;
    const available = Math.max(0, l.factor === 1 ? Math.floor(fits * 1000) / 1000 : Math.floor(fits + 1e-9));
    return available === l.available ? l : { ...l, available };
  });
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
      if (parsed?.key && Array.isArray(parsed.lines)) {
        // Carts saved before units: every line is in the base unit.
        parsed.lines = parsed.lines.map((l) => l.key ? l : {
          ...l, key: lineKey(l.productId, null), productUnitId: null, factor: 1, stock: l.available, units: [],
          base: { productUnitId: null, unitName: l.unitName, factor: 1, retailPrice: l.retailPrice, wholesalePrice: l.wholesalePrice, minSalePrice: l.minSalePrice },
        });
        return parsed;
      }
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
  // A price lowered below the product's price is saved as a discount on the line (the server does the same).
  const priceReductions = round2(cart.lines.reduce((s, l) => s + l.quantity * Math.max(0, tierPrice(l, cart.tier) - l.unitPrice), 0));
  const items = cart.lines.reduce((s, l) => s + l.quantity * l.factor, 0);
  return { gross, lineDiscounts, priceReductions, subtotal: round2(gross - lineDiscounts), listTotal, items };
}

type ProductLike = Pick<ProductListItem, 'id' | 'displayName' | 'sku' | 'unitName' | 'imageFileId' | 'quantity' | 'retailPrice' | 'wholesalePrice' | 'minSalePrice' | 'averageCost' | 'units'>;

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

  /** Adds one (of the base unit, or of `unit`). Returns an Arabic message when stock does not allow it. */
  const add = useCallback((p: ProductLike, quantity = 1, unit: ProductUnit | null = null): string | null => {
    let error: string | null = null;
    setCart((c) => {
      const key = lineKey(p.id, unit?.id ?? null);
      const factor = unit?.factor ?? 1;
      const existing = c.lines.find((l) => l.key === key);
      const wanted = (existing?.quantity ?? 0) + quantity;
      const others = c.lines.filter((l) => l.productId === p.id && l.key !== key).reduce((s, l) => s + l.quantity * l.factor, 0);
      if (wanted * factor + others > p.quantity) {
        const left = Math.max(0, p.quantity - others);
        error = left <= 0 ? `«${p.displayName}» غير متوفر في المخزون`
          : unit ? `المتوفر من «${p.displayName}» ${left} ${p.unitName} فقط — لا يكفي ${wanted} ${unit.unitName}`
          : `المتوفر من «${p.displayName}» هو ${left} فقط`;
        return c;
      }
      if (existing) return { ...c, lines: withAvailability(c.lines.map((l) => (l === existing ? { ...l, quantity: wanted, stock: p.quantity } : l))) };
      const base: UnitChoice = { productUnitId: null, unitName: p.unitName, factor: 1, retailPrice: p.retailPrice, wholesalePrice: p.wholesalePrice, minSalePrice: p.minSalePrice };
      const choice = unit ? unitChoices({ base, units: p.units ?? [] }).find((u) => u.productUnitId === unit.id) ?? base : base;
      const line: CartLine = {
        key,
        productId: p.id,
        productUnitId: choice.productUnitId,
        factor: choice.factor,
        stock: p.quantity,
        base,
        units: p.units ?? [],
        name: p.displayName,
        sku: p.sku,
        unitName: choice.unitName,
        imageFileId: p.imageFileId,
        available: 0,
        retailPrice: choice.retailPrice,
        wholesalePrice: choice.wholesalePrice,
        minSalePrice: choice.minSalePrice,
        cost: p.averageCost == null ? null : p.averageCost * choice.factor,
        quantity,
        unitPrice: c.tier === 'Wholesale' ? choice.wholesalePrice : choice.retailPrice,
        customPrice: false,
        lineDiscount: 0,
      };
      return { ...c, lines: withAvailability([line, ...c.lines]) };
    });
    return error;
  }, []);

  const update = useCallback((key: string, patch: Partial<CartLine>) => {
    setCart((c) => ({ ...c, lines: withAvailability(c.lines.map((l) => (l.key === key ? { ...l, ...patch } : l))) }));
  }, []);

  const remove = useCallback((key: string) => setCart((c) => ({ ...c, lines: withAvailability(c.lines.filter((l) => l.key !== key)) })), []);

  /** Sells the line in another unit (piece <-> dozen): its prices follow; a line already in that unit absorbs it. Returns an error message. */
  const changeUnit = useCallback((key: string, productUnitId: number | null): string | null => {
    let error: string | null = null;
    setCart((c) => {
      const line = c.lines.find((l) => l.key === key);
      const choice = line && unitChoices(line).find((u) => u.productUnitId === productUnitId);
      if (!line || !choice || line.productUnitId === productUnitId) return c;
      const newKey = lineKey(line.productId, productUnitId);
      const target = c.lines.find((l) => l.key === newKey);
      const quantity = target ? target.quantity + 1 : 1;
      const others = c.lines.filter((l) => l.productId === line.productId && l.key !== key && l.key !== newKey).reduce((s, l) => s + l.quantity * l.factor, 0);
      if (quantity * choice.factor + others > line.stock) {
        error = `المتوفر من «${line.name}» لا يكفي ${choice.unitName}`;
        return c;
      }
      const switched: CartLine = {
        ...line, key: newKey, productUnitId, factor: choice.factor, unitName: choice.unitName,
        retailPrice: choice.retailPrice, wholesalePrice: choice.wholesalePrice, minSalePrice: choice.minSalePrice,
        cost: line.cost == null ? null : (line.cost / line.factor) * choice.factor,
        quantity, unitPrice: c.tier === 'Wholesale' ? choice.wholesalePrice : choice.retailPrice, customPrice: false, lineDiscount: 0,
      };
      const lines = c.lines.filter((l) => l.key !== newKey).map((l) => (l.key === key ? switched : l));
      return { ...c, lines: withAvailability(lines) };
    });
    return error;
  }, []);

  const setTier = useCallback(
    (tier: PriceTier) =>
      setCart((c) => ({ ...c, tier, lines: c.lines.map((l) => (l.customPrice ? l : { ...l, unitPrice: tierPrice(l, tier) })) })),
    [],
  );

  // A finished sale's price type is not carried to the next customer: every new invoice starts at retail («مفرق»).
  const reset = useCallback(() => setCart(() => newCart()), []);

  return { cart, add, update, remove, changeUnit, setTier, reset };
}
