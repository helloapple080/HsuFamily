// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { itineraryInput, memberInput, itineraryQuery, toRecordValues } from '../family-records'

describe('精簡版資料邊界', () => {
  it('保存民用日期，不受時區影響，拒絕不存在的日期', () => {
    const input = { name: '測試家人', title: '長輩', birthday: '1985-10-01T00:00:00.000Z', sortOrder: 0, isActive: true }
    expect(memberInput.safeParse({ ...input, birthday: '1985-02-30T00:00:00.000Z' }).success).toBe(false)
    expect(memberInput.safeParse({ ...input, birthday: '0000-01-01' }).success).toBe(false)
    expect(toRecordValues('members', memberInput.parse(input))).toMatchObject({ kind: 'members', civil_date: '1985-10-01', label: '測試家人' })
  })

  it('拒絕錯序日期、外部照片網址與過長搜尋字串', () => {
    const input = { title: '踏青', date: '2026-11-12', tags: [], participants: [], photos: [] }
    expect(itineraryInput.safeParse(input).success).toBe(true)
    expect(itineraryInput.safeParse({ ...input, endDate: '2026-11-11' }).success).toBe(false)
    expect(itineraryInput.safeParse({ ...input, photos: ['https://unknown.test/track'] }).success).toBe(false)
    expect(itineraryQuery.safeParse({ keyword: '字'.repeat(101) }).success).toBe(false)
    expect(itineraryQuery.safeParse({ cursorDate: '2026-11-11', cursorId: 'not-a-uuid' }).success).toBe(false)
  })
})
