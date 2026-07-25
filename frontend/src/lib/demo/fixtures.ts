import type { CalendarEvent, Itinerary } from '@/types/event'
import type { Member } from '@/types/member'

function freezeItems<T extends object>(items: T[]): readonly Readonly<T>[] {
  items.forEach(Object.freeze)
  return Object.freeze(items)
}

export const demoMembers = freezeItems<Member>([
  {
    id: 'demo-member-1', name: '示範長輩', title: '家族長輩', birthday: '1958-03-18',
    bio: '公開展示用虛構人物；demo-elder@example.invalid', contactInfo: { phone: '0900-000-000', lineId: 'demo-elder' },
    bloodType: 'O', sortOrder: 1, isActive: true,
  },
  {
    id: 'demo-member-2', name: '示範成員甲', title: '家庭成員', birthday: '1985-07-12',
    bio: '喜歡登山與家庭聚會。', contactInfo: { phone: '0900-000-001', lineId: 'demo-a' },
    bloodType: 'A', sortOrder: 2, isActive: true,
  },
  {
    id: 'demo-member-3', name: '示範成員乙', title: '家庭成員', birthday: '1988-11-05',
    bio: '負責準備聚會照片。', contactInfo: { phone: '0900-000-002', lineId: 'demo-b' },
    bloodType: 'B', sortOrder: 3, isActive: true,
  },
  {
    id: 'demo-member-4', name: '示範晚輩', title: '家族晚輩', birthday: '2015-01-24',
    bio: '喜歡畫畫與戶外活動。', contactInfo: null, bloodType: null, sortOrder: 4, isActive: true,
  },
])

export const demoEvents = freezeItems<CalendarEvent>([
  {
    id: 'demo-event-1', title: '夏季家族聚餐', description: '公開展示用的虛構聚會。', eventType: 'Meeting',
    startDate: '2026-07-26T10:30:00.000Z', endDate: '2026-07-26T13:00:00.000Z', isAllDay: false,
    isLunarDate: false, relatedMemberIds: ['demo-member-1', 'demo-member-2'], tags: ['聚餐', '示範'], color: '#2563eb',
    createdBy: 'demo', createdAt: '2026-07-01T00:00:00.000Z', updatedAt: '2026-07-01T00:00:00.000Z',
  },
  {
    id: 'demo-event-2', title: '農曆初一祭祖提醒', description: '示範台灣家族文化提醒。', eventType: 'Worship',
    startDate: '2026-08-13T00:00:00.000Z', isAllDay: true, isLunarDate: true,
    lunarDate: { year: 2026, month: 7, day: 1, isLeapMonth: false, displayText: '農曆七月初一' },
    worshipDetails: { deity: '祖先', offerings: ['鮮花', '水果'], preparations: ['整理供桌'], notes: '內容皆為展示資料' },
    relatedMemberIds: [], tags: ['祭祖', '示範'], color: '#ca8a04', createdBy: 'demo',
    createdAt: '2026-07-01T00:00:00.000Z', updatedAt: '2026-07-01T00:00:00.000Z',
  },
  {
    id: 'demo-event-3', title: '示範成員甲生日', eventType: 'Birthday', startDate: '2026-07-12T00:00:00.000Z',
    isAllDay: true, isLunarDate: false, relatedMemberIds: ['demo-member-2'], tags: ['生日'], color: '#db2777',
    createdBy: 'demo', createdAt: '2026-07-01T00:00:00.000Z', updatedAt: '2026-07-01T00:00:00.000Z',
  },
])

export const demoItineraries = freezeItems<Itinerary>([
  {
    id: 'demo-itinerary-1', title: '北海岸一日遊', description: '示範家庭旅遊行程。', date: '2026-08-02T01:00:00.000Z',
    endDate: '2026-08-02T10:00:00.000Z', location: '北海岸示範景點', tags: ['一日遊', '戶外'],
    participants: [{ memberId: 'demo-member-2', name: '示範成員甲' }, { memberId: 'demo-member-4', name: '示範晚輩' }],
    photos: [], notes: '所有地點與人物資料均為示範。', createdBy: 'demo',
    createdAt: '2026-07-01T00:00:00.000Z', updatedAt: '2026-07-01T00:00:00.000Z',
  },
  {
    id: 'demo-itinerary-2', title: '秋季家庭健行', description: '適合全家的簡易步道。', date: '2026-10-18T00:30:00.000Z',
    location: '示範森林步道', tags: ['健行', '家庭'], participants: [{ memberId: 'demo-member-3', name: '示範成員乙' }],
    photos: [], notes: '出發前確認天氣。', createdBy: 'demo', createdAt: '2026-07-01T00:00:00.000Z', updatedAt: '2026-07-01T00:00:00.000Z',
  },
])
