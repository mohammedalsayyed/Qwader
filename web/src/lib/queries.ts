import { useQuery } from '@tanstack/react-query';
import { lookupsApi, settingsApi } from '@/api/endpoints';

/** Reference lists (units, categories, accounts…), cached for the session and refreshed after edits. */
export function useLookups() {
  // Re-read when the app comes back to the screen: an account added on another device shows without reopening the app.
  return useQuery({ queryKey: ['lookups'], queryFn: lookupsApi.get, staleTime: 60_000, refetchOnWindowFocus: true });
}

export function useStoreSettings() {
  return useQuery({ queryKey: ['settings'], queryFn: settingsApi.get, staleTime: 60_000, refetchOnWindowFocus: true });
}
