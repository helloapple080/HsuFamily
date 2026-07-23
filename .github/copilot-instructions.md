# GitHub Copilot Instructions — 許氏入口網 (Hsu Family Portal)

## Project Context
This is the **許氏入口網 (Hsu Family Portal)** monorepo with a C# .NET 10 backend (Clean Architecture) and a Next.js 16 frontend (App Router + TypeScript). It targets the Hsu family and includes lunar calendar integration, worship reminders, and real-time calendar sync via SignalR. The default administrator is 許浴宸.

## Tech Constraints
- Backend: .NET 10 Web API, MongoDB (official driver), Argon2id, JWT, SignalR
- Frontend: Next.js 16.2, React 19, Tailwind CSS v4, shadcn/ui (base-nova), Zustand, React Query, Framer Motion
- Language: TypeScript (strict mode), C# 13 (nullable enabled)

## Code Style Rules
1. **C#**: async/await only, XML doc comments, ApiResponse<T> wrappers, UTC everywhere
2. **TypeScript**: No `any` types, Server Components first, `'use client'` only when needed
3. **Commits**: Use Conventional Commits (feat/fix/refactor/docs/chore)
4. **Security**: RBAC on all endpoints, Argon2id for passwords, no secrets in frontend
5. **UI Language**: Traditional Chinese (繁體中文)

## Architecture Decision Records
- Users and Members are separate collections (not all members need accounts)
- Events (calendar) and Itineraries (historical logs) are separate collections
- Itinerary pagination uses cursor-based pagination (not skip/limit)
- Refresh Token uses rotation pattern with embedded storage in User document
- Frontend state: Zustand for client state, React Query for server state

## When Writing Backend Code
- Follow Clean Architecture: Domain → Application → Infrastructure → Api
- New entities go in `FamilyPortal.Domain/Entities/`
- New interfaces go in `FamilyPortal.Application/Interfaces/`
- MongoDB implementations go in `FamilyPortal.Infrastructure/MongoDB/`
- Controllers go in `FamilyPortal.Api/Controllers/`
- Register DI in `FamilyPortal.Infrastructure/DependencyInjection.cs`

## When Writing Frontend Code
- Check `node_modules/next/dist/docs/` for Next.js 16 API changes
- Use shadcn/ui components from `@/components/ui/`
- Feature components go in `@/components/features/{feature}/`
- API calls use React Query hooks in `@/hooks/`
- Global state uses Zustand stores in `@/stores/`
