/// 農曆日期
export interface LunarDate {
  year: number
  month: number
  day: number
  isLeapMonth: boolean
  displayText: string
}

/// 聯絡資訊
export interface ContactInfo {
  phone?: string | null
  lineId?: string | null
}

/// 家庭成員
export interface Member {
  id: string
  name: string
  title: string
  birthday: string
  lunarBirthday?: LunarDate | null
  avatarUrl?: string | null
  bio?: string | null
  contactInfo?: ContactInfo | null
  bloodType?: string | null
  sortOrder: number
  isActive: boolean
}

/// 建立/更新成員請求
export interface UpsertMemberRequest {
  name: string
  title: string
  birthday: string
  lunarBirthday?: LunarDate | null
  avatarUrl?: string | null
  bio?: string | null
  contactInfo?: ContactInfo | null
  bloodType?: string | null
  sortOrder: number
  isActive: boolean
}
