export type PhotoKind = 'members' | 'itineraries' | 'worship'

export function maxPhotoSlots(kind: PhotoKind) {
  return kind === 'members' ? 1 : 5
}
