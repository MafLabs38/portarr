import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';

/** What the top consumers of a machine are ranked by. */
export type ConsumerSort = 'cpu' | 'memory';

interface ConsumerSortState {
  sort: ConsumerSort;
  setSort: (sort: ConsumerSort) => void;
}

/** The ranking of the top consumers, kept per browser and shared by every machine card (CPU by default). */
export const useConsumerSortStore = create<ConsumerSortState>()(
  persist(
    (set) => ({
      sort: 'cpu',
      setSort: (sort) => set({ sort }),
    }),
    { name: 'portarr.infrastructure', storage: createJSONStorage(() => localStorage) },
  ),
);
