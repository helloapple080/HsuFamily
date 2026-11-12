// @vitest-environment node
import { createClient } from '@supabase/supabase-js'
import { expect, it } from 'vitest'
import { eventInput } from '../event-contract'
import { createEventStore } from '../event-store'

const id = '10000000-0000-4000-8000-000000000001'
const startsAt = '2026-11-12T00:00:00+00:00'
const details = {
  title: '祭祖', eventType: 'Worship', isAllDay: true, isLunarDate: false,
  relatedMemberIds: [], tags: [],
}
const row = {
  id, starts_at: startsAt, ends_at: null, details, created_by: id, created_at: startsAt, updated_at: startsAt,
}

it('透過實際 Supabase SDK 傳送有界重疊查詢，並維持日曆 API 的日期與回應欄位', async () => {
  const client = createClient('https://test.supabase.invalid', 'supabase-test-key', {
    global: {
      fetch: async (input, options) => {
        const { pathname, searchParams } = new URL(String(input))
        expect(pathname).toBe('/rest/v1/calendar_events')
        if (options?.method === 'POST') {
          expect(JSON.parse(String(options.body))).toMatchObject({ starts_at: '2026-11-12T00:00:00.000Z', details })
          return Response.json([row])
        }
        if (options?.method === 'DELETE') return Response.json([{ id }])
        if (searchParams.get('id')) {
          expect(searchParams.get('id')).toBe(`eq.${id}`)
          return Response.json([row])
        }
        expect(searchParams.get('limit')).toBe('501')
        if (searchParams.get('repeat_kind') === 'is.null') {
          expect(searchParams.get('starts_at')).toBe('lte.2026-11-30T23:59:59.999Z')
          expect(searchParams.get('overlap_end')).toBe('gte.2026-11-01T00:00:00.000Z')
          return Response.json([row])
        }
        expect(searchParams.get('repeat_kind')).toBe('not.is.null')
        expect(searchParams.get('or')).toBe('(repeat_kind.in.(LunarYearly,LunarMonthly),starts_at.lte.2026-11-30T23:59:59.999Z)')
        return Response.json([])
      },
    },
  })
  const store = createEventStore(client)
  const expected = { id, title: '祭祖', startDate: '2026-11-12T00:00:00.000Z', isOccurrence: false }
  await expect(store.list('2026-11-01', '2026-11-30')).resolves.toMatchObject([expected])
  await expect(store.get(id)).resolves.toMatchObject(expected)
  await expect(store.save(eventInput.parse({ ...details, startDate: startsAt }))).resolves.toMatchObject(expected)
  await expect(store.remove(id)).resolves.toBe(true)
  await expect(store.list('2026-11-30', '2026-11-01')).rejects.toThrow()
})
