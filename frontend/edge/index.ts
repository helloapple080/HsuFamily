import { readPortalUser, verifyAccess } from './access'
import { handlePhotos } from './photos'
import { handleRecords } from './records'
import { createPortal, type PortalEnv } from './router'

const route = createPortal({ verify: verifyAccess, readUser: readPortalUser, records: handleRecords, photos: handlePhotos })

const worker = {
  fetch(request: Request, env: PortalEnv) {
    return route(request, env)
  },
}

export default worker
