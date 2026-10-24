import type { ReactNode } from 'react'
import styles from './Table.module.css'

export interface Column<T> {
  header: ReactNode
  cell: (row: T) => ReactNode
  align?: 'right'
}

interface Props<T> {
  rows: T[] | undefined
  columns: Column<T>[]
  rowKey: (row: T) => string
  empty?: ReactNode
}

export function Table<T>({ rows, columns, rowKey, empty = 'Nothing here yet' }: Props<T>) {
  if (rows && rows.length === 0) {
    return <div className={styles.empty}>{empty}</div>
  }

  return (
    <div className={styles.wrap}>
      <table className={styles.table}>
        <thead>
          <tr>
            {columns.map((column, i) => (
              <th key={i} className={column.align === 'right' ? styles.right : undefined}>
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows?.map((row) => (
            <tr key={rowKey(row)}>
              {columns.map((column, i) => (
                <td key={i} className={column.align === 'right' ? styles.right : undefined}>
                  {column.cell(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
