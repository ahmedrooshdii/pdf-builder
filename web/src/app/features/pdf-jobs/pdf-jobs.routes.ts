import { Routes } from '@angular/router';

/** The feature's public surface: everything else in this folder is private to it. */
export default [
  {
    path: '',
    title: 'Jobs',
    loadComponent: () => import('./feature/jobs/jobs.page').then((m) => m.JobsPage),
  },
  {
    path: 'new',
    title: 'New PDF',
    loadComponent: () => import('./feature/new-job/new-job.page').then((m) => m.NewJobPage),
  },
  {
    path: ':jobId',
    title: 'Job',
    loadComponent: () => import('./feature/job-detail/job-detail.page').then((m) => m.JobDetailPage),
  },
] satisfies Routes;
