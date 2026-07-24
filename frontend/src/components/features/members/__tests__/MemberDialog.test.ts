import { describe, expect, it } from 'vitest'
import {
  buildMemberPayload,
  memberSchema,
} from '@/components/features/members/MemberDialog'

describe('buildMemberPayload', () => {
  it('uses the selected Gregorian birthday year and a complete lunar display value', () => {
    const payload = buildMemberPayload(memberSchema.parse({
      name: '許浴宸',
      title: '管理員',
      birthday: '1990-03-05',
      lunarBirthdayMonth: 4,
      lunarBirthdayDay: 15,
      isLunarLeapMonth: true,
    }))

    expect(payload.birthday).toBe('1990-03-05T00:00:00.000Z')
    expect(payload.lunarBirthday).toEqual({
      year: 1990,
      month: 4,
      day: 15,
      isLeapMonth: true,
      displayText: '農曆閏四月十五',
    })
  })

  it('requires a Gregorian birthday', () => {
    const result = memberSchema.safeParse({
      name: '許浴宸',
      title: '管理員',
      birthday: '',
    })

    expect(result.success).toBe(false)
  })

  it.each([
    { lunarBirthdayMonth: 1 },
    { lunarBirthdayDay: 1 },
  ])('requires lunar month and day together', (lunarBirthday) => {
    const result = memberSchema.safeParse({
      name: '許浴宸',
      title: '管理員',
      birthday: '1990-03-05',
      ...lunarBirthday,
    })

    expect(result.success).toBe(false)
  })
})
