'use client'

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import api from '@/lib/api'
import { householdLimit, householdNote, type HouseholdInput, type HouseholdNote } from '@/lib/hosted/household-contract'
import { versionHeaders } from '@/lib/recordVersion'
import { useAuthStore } from '@/stores/authStore'
import type { ApiResponse } from '@/types/api'

type NoteVersion = Pick<HouseholdNote, 'id' | 'version'>
interface SaveNote { input: HouseholdInput; target?: NoteVersion }
const url = '/api/hosted/household'
const noteList = z.array(householdNote).max(householdLimit)

export function useHouseholdNotes() {
  const userId = useAuthStore((state) => state.user?.id)
  const queryKey = ['hosted-household', userId] as const
  const client = useQueryClient()
  const refresh = () => client.invalidateQueries({ queryKey })

  const notes = useQuery({
    queryKey, enabled: Boolean(userId), staleTime: 0,
    queryFn: async ({ signal }) => {
      const { data } = await api.get<ApiResponse<unknown>>(url, { signal })
      return noteList.parse(data.data)
    },
  })
  const save = useMutation({
    mutationFn: async ({ target, input }: SaveNote) => {
      if (target) await api.put(`${url}/${target.id}`, input, versionHeaders(target.version))
      else await api.post(url, input)
    }, onSuccess: refresh,
  })
  const remove = useMutation({
    mutationFn: async ({ id, version }: NoteVersion) => { await api.delete(`${url}/${id}`, versionHeaders(version)) },
    onSuccess: refresh,
  })
  return { notes, save, remove }
}
