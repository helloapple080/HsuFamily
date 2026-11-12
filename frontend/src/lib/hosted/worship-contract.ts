import { z } from 'zod'

export const worshipInput = z.strictObject({
  title: z.string().trim().min(1).max(100),
  lunarMonth: z.number().int().min(1).max(12).nullable(),
  lunarDay: z.number().int().min(1).max(30),
  leapMonthPolicy: z.enum(['RegularOnly', 'LeapOnly', 'Both']),
  notes: z.string().trim().max(2000),
})

export const worshipRule = worshipInput.extend({ id: z.uuid() })
export type WorshipInput = z.infer<typeof worshipInput>
export type WorshipRule = z.infer<typeof worshipRule>
