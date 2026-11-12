import type { SupabaseClient } from '@supabase/supabase-js'
import { readFamilyRecord, toRecordValues, type RecordInput, type RecordKind, type RecordQuery } from './family-records'

const columns = 'id,details,created_by,created_at,updated_at'

export function createFamilyStore(client: SupabaseClient) {
  const table = () => client.from('family_records')
  return {
    async list(kind: RecordKind, options: RecordQuery) {
      const query = table().select(columns).eq('kind', kind)
      if (kind === 'members') {
        // 家族規模有限；仍設上限以免單一請求取得無限資料。
        query.order('position').order('id').limit(500)
      } else {
        if (options.keyword) {
          // PostgREST 同時接受 * 作為 LIKE 萬用字元，換成字面篩選以避免不必要的掃描。
          const needle = options.keyword.replaceAll('*', '').replace(/[\\%_]/g, '\\$&')
          if (!needle) return []
          query.ilike('search_text', `%${needle}%`)
        }
        if (options.cursorDate && options.cursorId) {
          query.or(`civil_date.lt.${options.cursorDate},and(civil_date.eq.${options.cursorDate},id.lt.${options.cursorId})`)
        }
        query.order('civil_date', { ascending: false }).order('id', { ascending: false }).limit(options.limit)
      }
      const { data, error } = await query
      if (error) throw error
      return data.map((row) => readFamilyRecord(kind, row))
    },
    async save(kind: RecordKind, input: RecordInput, id?: string) {
      const { kind: recordKind, ...values } = toRecordValues(kind, input)
      const query = id ? table().update(values).eq('kind', kind).eq('id', id) : table().insert({ kind: recordKind, ...values })
      const { data, error } = await query.select(columns).maybeSingle()
      if (error) throw error
      return data ? readFamilyRecord(kind, data) : null
    },
    async remove(kind: RecordKind, id: string) {
      const { data, error } = await table().delete().eq('kind', kind).eq('id', id).select('id').maybeSingle()
      if (error) throw error
      return data !== null
    },
  }
}

export type FamilyStore = ReturnType<typeof createFamilyStore>
