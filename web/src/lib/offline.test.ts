import { describe, expect, it } from 'vitest';
import type { OfflineProduct } from '@/api/sales';
import { findByCode, searchCustomers, searchProducts, snapshotAge, type OfflineSnapshot } from './offline';

const product = (id: number, displayName: string, search: string, sku: string, barcode: string | null = null): OfflineProduct => ({
  id, sku, barcode, displayName, search, unitName: 'قطعة', imageFileId: null, quantity: 5, retailPrice: 10, wholesalePrice: 8, minSalePrice: 7, averageCost: null,
});

const snapshot = {
  generatedAt: new Date('2026-11-04T08:00:00Z').toISOString(),
  maxHours: 24,
  products: [
    product(1, 'غلاية كهربائية', 'غلايه كهربائيه p-001', 'P-001', '6291041500213'),
    product(2, 'مكواة بخار', 'مكواه بخار p-002', 'P-002'),
    product(3, 'غلاية زجاج', 'غلايه زجاج p-010', 'P-010'),
  ],
  customers: [
    { id: 7, code: 'C-00007', name: 'أحمد سالم', phone: '0599123456', search: 'احمد سالم', balance: 0 },
  ],
} as unknown as OfflineSnapshot;

describe('offline till search', () => {
  it('matches every word after Arabic normalization, like the server', () => {
    expect(searchProducts(snapshot, 'غلاية').map((p) => p.id)).toEqual([3, 1]); // newest first, as on the server
    expect(searchProducts(snapshot, 'غلايه زجاج').map((p) => p.id)).toEqual([3]);
  });

  it('ignores spaces typed or left out', () => {
    expect(searchProducts(snapshot, 'غلايهزجاج').map((p) => p.id)).toEqual([3]);
    expect(searchProducts(snapshot, 'مكو اه بخار').map((p) => p.id)).toEqual([2]);
  });

  it('puts an exact code first and finds by barcode', () => {
    expect(searchProducts(snapshot, 'p-010')[0].id).toBe(3);
    expect(findByCode(snapshot, '6291041500213')?.id).toBe(1);
    expect(findByCode(snapshot, 'p-002')?.id).toBe(2);
    expect(findByCode(snapshot, 'x')).toBeUndefined();
  });

  it('finds customers by name or phone', () => {
    expect(searchCustomers(snapshot, 'احمد').map((c) => c.id)).toEqual([7]);
    expect(searchCustomers(snapshot, '0599123').map((c) => c.id)).toEqual([7]);
  });

  it('stops offline selling once the copy is older than the store limit', () => {
    const at = (h: number) => Date.parse(snapshot.generatedAt) + h * 3_600_000;
    expect(snapshotAge(snapshot, at(23)).usable).toBe(true);
    expect(snapshotAge(snapshot, at(25)).usable).toBe(false);
    expect(snapshotAge(null).usable).toBe(false);
  });
});
