import type { SupabaseClient } from '@supabase/supabase-js'
import { z } from 'zod'
import type { CalendarEvent } from '@/types/event'
import { eventInput, eventQuery, type EventInput } from './event-contract'
import { expandCalendarEvent } from './event-recurrence'

const columns = 'id,starts_at,ends_at,details,created_by,created_at,updated_at'
const readLimit = 500
const responseLimit = 1000

const rowSchema = z.object({
  id: z.uuid(), starts_at: z.string(), ends_at: z.string().nullable(),
  details: z.record(z.string(), z.unknown()),
  created_by: z.uuid(), created_at: z.string(), updated_at: z.string(),
})

function parseRow(value: unknown): CalendarEvent {
  const row = rowSchema.parse(value)
  const event = eventInput.parse({ ...row.details, startDate: row.starts_at, endDate: row.ends_at })
  return {
    ...event,
    id: row.id, createdBy: row.created_by, createdAt: row.created_at, updatedAt: row.updated_at,
    isOccurrence: false, sourceRuleId: null,
  }
}

export function createEventStore(client: SupabaseClient) {
  const table = () => client.from('calendar_events')
  return {
    async list(start: string, end: string) {
      const range = eventQuery.parse({ start, end })
      const candidates = () => table().select(columns).order('starts_at').order('id').limit(readLimit + 1)
      const [single, repeating] = await Promise.all([
        candidates().is('repeat_kind', null).lte('starts_at', range.end).gte('overlap_end', range.start),
        // 國曆週期不可能早於起始日；農曆週期仍須納入來源農曆月內已生效的日期。
        candidates().not('repeat_kind', 'is', null)
          .or(`repeat_kind.in.(LunarYearly,LunarMonthly),starts_at.lte.${range.end}`),
      ])
      if (single.error) throw single.error
      if (repeating.error) throw repeating.error
      if (single.data.length > readLimit || repeating.data.length > readLimit) throw new RangeError('行事曆事件已超過單次讀取上限')
      const result = single.data.map(parseRow)
      for (const row of repeating.data) {
        result.push(...expandCalendarEvent(parseRow(row), range.start, range.end))
        if (result.length > responseLimit) throw new RangeError('行事曆事件已超過單次回應上限')
      }
      return result.sort((a, b) => a.startDate.localeCompare(b.startDate) || a.id.localeCompare(b.id))
    },
    async get(id: string) {
      const { data, error } = await table().select(columns).eq('id', id).maybeSingle()
      if (error) throw error
      return data ? parseRow(data) : null
    },
    async save(input: EventInput, id?: string) {
      const { startDate, endDate, ...details } = input
      const values = { starts_at: startDate, ends_at: endDate ?? null, details }
      const query = id ? table().update(values).eq('id', id) : table().insert(values)
      const { data, error } = await query.select(columns).maybeSingle()
      if (error) throw error
      return data ? parseRow(data) : null
    },
    async remove(id: string) {
      const { data, error } = await table().delete().eq('id', id).select('id').maybeSingle()
      if (error) throw error
      return data !== null
    },
  }
}

export type EventStore = ReturnType<typeof createEventStore>
