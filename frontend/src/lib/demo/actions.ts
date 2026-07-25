import { demoReadOnlyMessage } from './config'

export async function rejectDemoWrite(): Promise<never> {
  throw new Error(demoReadOnlyMessage)
}
