const { MongoClient } = require('mongodb')
const dotenv = require('dotenv')
const path = require('path')

// 載入根目錄的 .env 檔案
dotenv.config({ path: path.join(__dirname, '../../../.env') })

const rootPassword = process.env.MONGO_ROOT_PASSWORD || 'ChangeMe2026'
const uri = `mongodb://familyportal_root:${rootPassword}@localhost:27017/FamilyPortal?authSource=admin`

async function updateAdmin() {
  const client = new MongoClient(uri)

  try {
    await client.connect()
    console.log('Connected to MongoDB')

    const database = client.db('FamilyPortal')
    const users = database.collection('Users')

    const result = await users.updateOne(
      { username: 'admin' },
      { $set: { displayName: '許浴宸' } }
    )
    console.log(result.matchedCount > 0 ? 'Success' : 'Fail')
  } catch (error) {
    console.error('Error updating admin:', error)
  } finally {
    await client.close()
  }
}

updateAdmin()
