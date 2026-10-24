import { z } from 'zod'

// те же правила, что у Identity на шлюзе, чтобы не гонять заведомо плохой пароль на сервер
export const credentialsSchema = z.object({
  userName: z.string().trim().min(1, 'Required').max(64),
  password: z.string().min(10, 'At least 10 characters'),
})

export type Credentials = z.infer<typeof credentialsSchema>
