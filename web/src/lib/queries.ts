import { useQuery } from '@tanstack/react-query';
import { lookupsApi, settingsApi } from '@/api/endpoints';

/** Reference lists (units, categories, accounts…), cached for the session and refreshed after edits. */
export function useLookups() {
  return useQuery({ queryKey: ['lookups'], queryFn: lookupsApi.get, staleTime: 5 * 60_000 });
}

export function useStoreSettings() {
  return useQuery({ queryKey: ['settings'], queryFn: settingsApi.get, staleTime: 5 * 60_000 });
}
