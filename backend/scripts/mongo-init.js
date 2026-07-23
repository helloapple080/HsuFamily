/* global db */

// 僅在 MongoDB 資料卷第一次初始化時，建立最小權限的應用程式帳號。
const appDatabase = db.getSiblingDB('FamilyPortal')

appDatabase.createUser({
  user: 'familyportal_app',
  pwd: process.env.MONGO_APP_PASSWORD,
  roles: [{ role: 'readWrite', db: 'FamilyPortal' }],
})
