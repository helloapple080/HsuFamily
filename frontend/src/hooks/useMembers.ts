'use client'

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import type { Member, UpsertMemberRequest } from '@/types/member'
import type { ApiResponse } from '@/types/api'
import api from '@/lib/api'
import { toast } from 'sonner'
import axios from 'axios'

export function useMembers() {
  return useQuery({
    queryKey: ['members'],
    queryFn: async () => {
      const res = await api.get<ApiResponse<Member[]>>('/api/members')
      if (!res.data.success) throw new Error(res.data.message)
      return res.data.data ?? []
    },
  })
}

export function useCreateMember() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (data: UpsertMemberRequest) => {
      const res = await api.post<ApiResponse<Member>>('/api/members', data)
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
    mutationFn: async ({ id, data }: { id: string; data: UpsertMemberRequest }) => {
      const res = await api.put<ApiResponse<Member>>(`/api/members/${id}`, data)
      if (!res.data.success) throw new Error(res.data.message)
      return res.data.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['members'] })
      toast.success('成員已更新')
    },
    onError: (error) => {
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
    mutationFn: async (id: string) => {
      const res = await api.delete<ApiResponse<boolean>>(`/api/members/${id}`)
      if (!res.data.success) throw new Error(res.data.message)
      return res.data.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['members'] })
      toast.success('成員已刪除')
    },
    onError: (error) => {
      if (axios.isAxiosError(error)) {
        toast.error(error.response?.data?.message || '刪除失敗')
      } else {
        toast.error((error as Error).message || '刪除失敗')
      }
    },
  })
}
