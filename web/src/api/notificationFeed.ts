import { api } from './client';

export type FeedTopic = 'None' | 'Transfers' | 'Stock' | 'Sales';

export interface FeedItem {
  id: number;
  topic: FeedTopic;
  title: string;
  body: string;
  /** Where tapping it goes (the invoice, transfers, the product, «بياناتي المالية»). */
  url: string;
  createdAt: string;
  read: boolean;
}

export interface FeedPage {
  items: FeedItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  unreadCount: number;
}

/** The signed-in user's own notifications, newest first. */
export const feedApi = {
  list: (page = 1, pageSize = 20) => api<FeedPage>('/notification-feed', { query: { page, pageSize } }),
  read: (id: number) => api<void>(`/notification-feed/${id}/read`, { method: 'POST' }),
  readAll: () => api<void>('/notification-feed/read-all', { method: 'POST' }),
};
