export type MediaVisibility = 'Public' | 'FamilyOnly'

export type MediaPurpose = 'MemberAvatar' | 'Worship' | 'Itinerary'

/** API 回傳的媒體公開資訊；內容一律經入口網 API 讀取。 */
export interface MediaAssetSummary {
  id: string
  contentType: 'image/jpeg' | 'image/png' | 'image/webp'
  width: number
  height: number
  visibility: MediaVisibility
  purpose: MediaPurpose
  ownerEntityId: string
}

export interface UploadMediaInput {
  file: File
  purpose: MediaPurpose
  ownerEntityId: string
  visibility: MediaVisibility
  onProgress?: (percentage: number) => void
}

export interface UpdateMediaVisibilityInput {
  id: string
  visibility: MediaVisibility
}
