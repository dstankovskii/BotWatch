import { z } from 'zod'

// zod 4 ускоряет схемы через new Function, а csp без unsafe-eval это запрещает и сыпет ошибки в консоль.
// отдельный модуль, импортируется в main.tsx первым: схемы создаются при импорте страниц, до тела main
z.config({ jitless: true })
