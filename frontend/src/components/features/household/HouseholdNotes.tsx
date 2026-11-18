'use client'

import { isAxiosError } from 'axios'
import { MapPin, Plus } from 'lucide-react'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { toast } from 'sonner'
import ConfirmDialog from '@/components/common/ConfirmDialog'
import ErrorState from '@/components/common/ErrorState'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { useHouseholdNotes } from '@/hooks/useHouseholdNotes'
import {
  householdCategories, householdCategory, householdFieldLimits, householdInput, householdLimit,
  type HouseholdInput, type HouseholdNote,
} from '@/lib/hosted/household-contract'
import { isVersionConflict, versionConflictMessage } from '@/lib/recordVersion'
import { useAuthStore } from '@/stores/authStore'
import type { ApiResponse } from '@/types/api'

const categories = Object.entries(householdCategories).map(([value, label]) => <option key={value} value={value}>{label}</option>)
const selectStyle = 'min-h-11 w-full rounded-lg border border-input bg-card px-3 text-base sm:text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring'
const clean = (text: string) => text.normalize('NFKC').trim().toLowerCase()
const newNote: HouseholdInput = { category: 'water', title: '', location: '', details: '' }

function NoteEditor({ note, pending, onCancel, onSave }: {
  note: HouseholdNote | null
  pending: boolean
  onCancel: () => void
  onSave: (input: HouseholdInput) => Promise<void>
}) {
  const [draft, setDraft] = useState<HouseholdInput>(() => note
    ? { category: note.category, title: note.title, location: note.location, details: note.details }
    : newNote)
  const [error, setError] = useState('')
  const [conflict, setConflict] = useState(false)
  const first = useRef<HTMLInputElement>(null)
  useEffect(() => { first.current?.focus() }, [])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending || conflict) return
    const parsed = householdInput.safeParse(draft)
    if (!parsed.success) {
      setError('請填寫標題與說明，並檢查欄位的格式與字數。')
      return
    }
    setError('')
    try {
      await onSave(parsed.data)
    } catch (problem) {
      if (isVersionConflict(problem)) {
        setError(versionConflictMessage)
        setConflict(true)
      } else if (isAxiosError<ApiResponse<null>>(problem) && problem.response?.data?.errors?.includes('HOUSEHOLD_LIMIT')) {
        setError(`已達 ${householdLimit} 筆上限。請先刪除不需要的備忘。`)
      } else {
        setError('目前無法儲存，已保留你輸入的內容。請稍後再試。')
      }
    }
  }

  return <form onSubmit={submit} className="rounded-xl border border-border bg-card p-4 sm:p-5" aria-labelledby="household-editor">
    <h2 id="household-editor" className="text-lg font-semibold">{note ? '編輯備忘' : '新增備忘'}</h2>
    <fieldset disabled={pending} className="mt-4 grid min-w-0 gap-4 sm:grid-cols-2">
      <div className="grid gap-2 sm:col-span-2">
        <Label htmlFor="note-title">標題</Label>
        <Input ref={first} autoComplete="off" className="min-h-11" id="note-title" required maxLength={householdFieldLimits.title} value={draft.title}
          onChange={(event) => setDraft({ ...draft, title: event.target.value })} />
      </div>
      <div className="grid gap-2">
        <Label htmlFor="note-category">分類</Label>
        <select id="note-category" className={selectStyle} value={draft.category} onChange={(event) => setDraft({ ...draft, category: householdCategory.parse(event.target.value) })}>{categories}</select>
      </div>
      <div className="grid gap-2">
        <Label htmlFor="note-location">位置（選填）</Label>
        <Input autoComplete="off" className="min-h-11" id="note-location" maxLength={householdFieldLimits.location} value={draft.location}
          onChange={(event) => setDraft({ ...draft, location: event.target.value })} />
      </div>
      <div className="grid gap-2 sm:col-span-2">
        <Label htmlFor="note-details">說明</Label>
        <Textarea className="min-h-28" id="note-details" required maxLength={householdFieldLimits.details} aria-describedby="note-privacy" value={draft.details}
          onChange={(event) => setDraft({ ...draft, details: event.target.value })} />
        <p id="note-privacy" className="text-sm text-muted-foreground">核准的家人都能看到；請勿填寫密碼、門鎖密碼或驗證碼。</p>
      </div>
      {error && <p className="text-sm text-destructive sm:col-span-2" role="alert">{error}</p>}
      <div className="flex flex-wrap justify-end gap-2 sm:col-span-2">
        <Button type="button" variant="outline" className="min-h-11" onClick={onCancel}>取消</Button>
        <Button type="submit" className="min-h-11" disabled={conflict}>{pending ? '儲存中…' : '儲存'}</Button>
      </div>
    </fieldset>
  </form>
}

export default function HouseholdNotes() {
  const isAdmin = useAuthStore((state) => state.isAdmin)
  const { notes, save, remove } = useHouseholdNotes()
  const [keyword, setKeyword] = useState('')
  const [category, setCategory] = useState('')
  // 固定開啟時的版本，不會因背景重新讀取而覆蓋另一人的變更。
  const [editor, setEditor] = useState<HouseholdNote | null | undefined>()
  const heading = useRef<HTMLHeadingElement>(null)
  const focusAfterClosing = useRef<string | null>(null)
  const term = clean(keyword)
  const visible = notes.data?.filter((note) => (!category || note.category === category)
    && (!term || [householdCategories[note.category], note.title, note.location, note.details].some((value) => clean(value).includes(term)))) ?? []

  useEffect(() => {
    if (editor === undefined && focusAfterClosing.current) {
      const target = document.getElementById(focusAfterClosing.current) ?? heading.current
      target?.focus()
      focusAfterClosing.current = null
    }
  }, [editor])

  function closeEditor(returnToEditorTrigger = false) {
    if (returnToEditorTrigger) focusAfterClosing.current = editor ? `note-edit-${editor.id}` : 'note-create'
    else heading.current?.focus()
    setEditor(undefined)
  }

  async function saveNote(input: HouseholdInput) {
    await save.mutateAsync({ input, target: editor ?? undefined })
    setKeyword('')
    setCategory('')
    closeEditor()
    toast.success('備忘已儲存')
  }

  return <section className="mx-auto max-w-3xl space-y-5" aria-labelledby="household-heading">
    <header className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
      <div className="space-y-1">
        <h1 id="household-heading" ref={heading} tabIndex={-1} className="text-2xl font-bold tracking-tight sm:text-3xl">家用備忘</h1>
        <p className="text-sm leading-relaxed text-muted-foreground">查找供水開關、店務資料與家中設備的使用資訊。</p>
      </div>
      {isAdmin && editor === undefined && <Button id="note-create" type="button" className="min-h-11 self-start sm:self-auto" onClick={() => setEditor(null)}>
        <Plus aria-hidden="true" /> 新增備忘
      </Button>}
    </header>
    {isAdmin && editor !== undefined && <NoteEditor key={editor?.id ?? 'new'} note={editor} pending={save.isPending} onCancel={() => closeEditor(true)} onSave={saveNote} />}
    {notes.isPending ? <p role="status" className="py-8 text-sm text-muted-foreground">正在載入家用備忘…</p>
      : notes.isError ? <ErrorState title="無法載入家用備忘" description="請確認仍在私人入口並重試。" onRetry={() => void notes.refetch()} />
      : notes.data.length === 0 ? <div className="space-y-2 rounded-xl border border-border bg-card p-5 sm:p-7">
        <h2 className="text-base font-semibold">還沒有家用備忘</h2>
        <p className="text-sm leading-relaxed text-muted-foreground">{isAdmin ? '可以先寫下設備的位置，以及家人在操作前需要知道的事。' : '管理員新增後，備忘會出現在這裡。'}</p>
      </div> : <>
        <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_10rem]">
          <div className="grid min-w-0 gap-2">
            <Label htmlFor="household-search">搜尋備忘</Label>
            <Input id="household-search" type="search" autoComplete="off" className="min-h-11" maxLength={120} placeholder="例如：三樓、濾芯" value={keyword} onChange={(event) => setKeyword(event.target.value)} />
          </div>
          <div className="grid gap-2">
            <Label htmlFor="household-filter">篩選分類</Label>
            <select id="household-filter" className={selectStyle} value={category} onChange={(event) => setCategory(event.target.value)}>
              <option value="">全部分類</option>{categories}
            </select>
          </div>
        </div>
        <p role="status" className="text-sm text-muted-foreground">{visible.length} 筆備忘</p>
        {visible.length === 0 ? <div className="space-y-1 py-4">
          <h2 className="font-semibold">找不到符合的備忘</h2>
          <p className="text-sm text-muted-foreground">試試其他關鍵字，或切換回全部分類。</p>
        </div> : <ul aria-label="家用備忘清單" className="divide-y divide-border rounded-xl border border-border bg-card">
          {visible.map((note) => <li key={note.id} className="min-w-0 space-y-3 p-4 sm:p-5">
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div className="min-w-0 space-y-2">
                <Badge variant="secondary">{householdCategories[note.category]}</Badge>
                <h2 className="text-base font-semibold wrap-anywhere">{note.title}</h2>
              </div>
              {isAdmin && editor === undefined && <div className="flex flex-wrap gap-1">
                <Button id={`note-edit-${note.id}`} variant="outline" className="min-h-11" aria-label={`編輯 ${note.title}`} onClick={() => setEditor(note)}>編輯</Button>
                <ConfirmDialog title="刪除這筆備忘？" description={`「${note.title}」刪除後無法從入口網復原。`} recordVersion={note.version}
                  trigger={<Button variant="ghost" className="min-h-11 text-destructive" aria-label={`刪除 ${note.title}`}>刪除</Button>}
                  onConfirm={async (version) => {
                    if (version === undefined) throw new Error('缺少備忘版本')
                    await remove.mutateAsync({ id: note.id, version })
                    heading.current?.focus()
                    toast.success('備忘已刪除')
                  }} />
              </div>}
            </div>
            {note.location && <p className="flex items-start gap-2 text-sm text-muted-foreground"><MapPin aria-hidden="true" className="mt-0.5 size-4 shrink-0" /><span className="min-w-0 wrap-anywhere">{note.location}</span></p>}
            <p className="whitespace-pre-wrap wrap-anywhere text-sm leading-relaxed">{note.details}</p>
          </li>)}
        </ul>}
      </>}
  </section>
}
