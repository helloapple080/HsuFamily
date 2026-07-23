import type { LunarDate } from './member'

/// 事件類型
export type EventType = 'General' | 'Birthday' | 'Festival' | 'Worship' | 'Anniversary' | 'Meeting' | 'Other'

/// 週期類型
export type RecurrenceType = 'Yearly' | 'Monthly' | 'LunarYearly' | 'LunarMonthly'

/// 週期規則
export interface RecurrenceRule {
  type: RecurrenceType
  interval: number
  lunarDays?: number[] | null
}

/// 拜拜細節
export interface WorshipDetails {
  deity?: string | null
  offerings: string[]
  preparations: string[]
  notes?: string | null
}

/// 行事曆事件
export interface CalendarEvent {
  id: string
  title: string
  description?: string | null
  eventType: EventType
  startDate: string
  endDate?: string | null
  isAllDay: boolean
  isLunarDate: boolean
  lunarDate?: LunarDate | null
  recurrenceRule?: RecurrenceRule | null
  worshipDetails?: WorshipDetails | null
  relatedMemberIds: string[]
  tags: string[]
  color?: string | null
  createdBy: string
  createdAt: string
  updatedAt: string
}

/// 建立/更新事件請求
export interface UpsertEventRequest {
  title: string
  description?: string | null
  eventType: EventType
  startDate: string
  endDate?: string | null
  isAllDay: boolean
  isLunarDate: boolean
  lunarDate?: LunarDate | null
  recurrenceRule?: RecurrenceRule | null
  worshipDetails?: WorshipDetails | null
  relatedMemberIds: string[]
  tags: string[]
  color?: string | null
}

/// 參與者快照
export interface ParticipantSnapshot {
  memberId: string
  name: string
}

/// 行程紀錄
export interface Itinerary {
  id: string
  title: string
  description?: string | null
  date: string
  endDate?: string | null
  location?: string | null
  tags: string[]
  participants: ParticipantSnapshot[]
  photos: string[]
  notes?: string | null
  linkedEventId?: string | null
  createdBy: string
  createdAt: string
  updatedAt: string
}

/// 建立/更新行程請求
export interface UpsertItineraryRequest {
  title: string
  description?: string | null
  date: string
  endDate?: string | null
  location?: string | null
  tags: string[]
  participants: ParticipantSnapshot[]
  photos: string[]
  notes?: string | null
  linkedEventId?: string | null
}
