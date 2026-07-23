/* global db */

// 每次部署皆冪等確認最小權限帳號，讓既有 MongoDB volume 也能安全升級。
const appPassword = process.env.MONGO_APP_PASSWORD
if (!appPassword) {
  throw new Error('MONGO_APP_PASSWORD is required')
}

const appDatabase = db.getSiblingDB('FamilyPortal')
const user = appDatabase.getUser('familyportal_app')
const roles = [{ role: 'readWrite', db: 'FamilyPortal' }]

if (user) {
  appDatabase.updateUser('familyportal_app', { pwd: appPassword, roles })
} else {
  appDatabase.createUser({ user: 'familyportal_app', pwd: appPassword, roles })
}
