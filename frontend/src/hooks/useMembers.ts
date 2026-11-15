'use client'

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import type { Member, UpsertMemberRequest } from '@/types/member'
import type { ApiResponse } from '@/types/api'
import api from '@/lib/api'
import { toast } from 'sonner'
import axios from 'axios'
import { isDemoMode } from '@/lib/demo/config'
import { demoMembers } from '@/lib/demo/fixtures'
import { rejectDemoWrite } from '@/lib/demo/actions'
import { isVersionConflict, versionConflictMessage, versionHeaders, type RecordVersion } from '@/lib/recordVersion'

export function useMembers() {
  return useQuery({
    queryKey: ['members'],
    queryFn: async ({ signal }) => {
      if (isDemoMode) return [...demoMembers]
      const res = await api.get<ApiResponse<Member[]>>('/api/hosted/members', { signal })
      if (!res.data.success) throw new Error(res.data.message)
      return res.data.data ?? []
    },
  })
}

export function useCreateMember() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (data: UpsertMemberRequest) => {
      if (isDemoMode) return rejectDemoWrite()
      const res = await api.post<ApiResponse<Member>>('/api/hosted/members', data)
      if (!res.data.success) throw new Error(res.data.message)
      return res.data.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['members'] })
      toast.success('成員已建立')
    },
    onError: (error) => {
      if (axios.isAxiosError(error)) {
        toast.error(error.response?.data?.message || '建立失敗')
      } else {
        toast.error((error as Error).message || '建立失敗')
      }
    },
  })
}

export function useUpdateMember() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({ id, data, version }: RecordVersion & { data: UpsertMemberRequest }) => {
      if (isDemoMode) return rejectDemoWrite()
      const res = await api.put<ApiResponse<Member>>(`/api/hosted/members/${id}`, data, versionHeaders(version))
      if (!res.data.success) throw new Error(res.data.message)
      return res.data.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['members'] })
      toast.success('成員已更新')
    },
    onError: (error) => {
      if (isVersionConflict(error)) return
      if (axios.isAxiosError(error)) {
        toast.error(error.response?.data?.message || '更新失敗')
      } else {
        toast.error((error as Error).message || '更新失敗')
      }
    },
  })
}

export function useDeleteMember() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (record: string | RecordVersion) => {
      if (isDemoMode) return rejectDemoWrite()
      const { id, version } = typeof record === 'string' ? { id: record } : record
      const res = await api.delete<ApiResponse<boolean>>(`/api/hosted/members/${id}`, versionHeaders(version))
      if (!res.data.success) throw new Error(res.data.message)
      return res.data.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['members'] })
      toast.success('成員已刪除')
    },
    onError: (error) => {
      if (isVersionConflict(error)) {
        toast.error(versionConflictMessage)
        return
      }
      if (axios.isAxiosError(error)) {
        toast.error(error.response?.data?.message || '刪除失敗')
      } else {
        toast.error((error as Error).message || '刪除失敗')
      }
    },
  })
}
