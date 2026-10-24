import { Link } from 'react-router'
import { PageHead } from './PageHead'

export function NotFoundPage() {
  return (
    <>
      <PageHead title="Not found" sub="This page does not exist or was deleted." />
      <Link to="/">Back to overview</Link>
    </>
  )
}
