import http from './http'

export type HealthStatus = 'healthy' | 'unhealthy'

export async function fetchApiLiveness(): Promise<HealthStatus> {
  try {
    const response = await http.get<string>('/api/health/live')
    return response.status === 200 ? 'healthy' : 'unhealthy'
  } catch {
    return 'unhealthy'
  }
}
