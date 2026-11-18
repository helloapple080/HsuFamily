import { z } from 'zod'

export const householdCategories = {
  water: '供水', store: '店務', equipment: '設備', other: '其他',
} as const
export const householdLimit = 200
export const householdFieldLimits = { title: 100, location: 120, details: 2000 } as const
export const householdCategory = z.enum(['water', 'store', 'equipment', 'other'])

// 詳細說明可換行；名稱和位置只能是單行純文字。
const singleLine = /^[^\u0000-\u001f\u007f]*$/u
const multiline = /^[^\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]*$/u

export const householdInput = z.strictObject({
  category: householdCategory,
  title: z.string().regex(singleLine).trim().min(1).max(householdFieldLimits.title),
  location: z.string().regex(singleLine).trim().max(householdFieldLimits.location),
  details: z.string().regex(multiline).trim().min(1).max(householdFieldLimits.details),
})
export const householdNote = householdInput.extend({
  id: z.uuid(), version: z.int().positive(), updatedAt: z.iso.datetime(),
})
export type HouseholdInput = z.infer<typeof householdInput>
export type HouseholdNote = z.infer<typeof householdNote>
export type HouseholdCategory = z.infer<typeof householdCategory>
