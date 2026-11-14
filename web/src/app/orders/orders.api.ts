/**
 * OrdersApi: the only place in the app that talks HTTP to the Orders API.
 *
 * - submit()       POST  /api/orders              returns the order plus `replayed`, true when the
 *                                                  server answered 200 with Idempotent-Replayed
 *                                                  (same order sent again, no duplicate created)
 * - list()         GET   /api/orders              order summaries, newest first
 * - get()          GET   /api/orders/{id}         one full order for the detail view
 * - changeStatus() PATCH /api/orders/{id}/status  sends the version the screen last saw, so a
 *                                                  stale screen gets a 409 instead of overwriting
 *
 * Every failure is converted into one ApiProblem shape (toProblem below), including
 * "server unreachable", so components handle errors one way.
 */

import { HttpClient, HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, throwError } from 'rxjs';
import { ApiProblem, CreateOrderRequest, Order, OrderStatus, OrderSummary, PagedResult } from './order.models';

export interface SubmitResult {
  order: Order;
  /** True when the reference had already been submitted with the same details. */
  replayed: boolean;
}

@Injectable({ providedIn: 'root' })
export class OrdersApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/orders';

  submit(request: CreateOrderRequest): Observable<SubmitResult> {
    return this.http.post<Order>(this.base, request, { observe: 'response' }).pipe(
      map((res: HttpResponse<Order>) => ({
        order: res.body!,
        replayed: res.status === 200 && res.headers.get('Idempotent-Replayed') === 'true',
      })),
      catchError(toProblem),
    );
  }

  list(pageSize = 100): Observable<PagedResult<OrderSummary>> {
    return this.http
      .get<PagedResult<OrderSummary>>(this.base, { params: { pageSize } })
      .pipe(catchError(toProblem));
  }

  get(id: string): Observable<Order> {
    return this.http.get<Order>(`${this.base}/${id}`).pipe(catchError(toProblem));
  }

  changeStatus(id: string, status: OrderStatus, expectedVersion: number, reason?: string): Observable<Order> {
    return this.http
      .patch<Order>(`${this.base}/${id}/status`, { status, expectedVersion, reason: reason || null })
      .pipe(catchError(toProblem));
  }
}

/** Normalise every failure into an ApiProblem so components only deal with one shape. */
function toProblem(err: HttpErrorResponse): Observable<never> {
  if (err.status === 0) {
    return throwError(() => <ApiProblem>{
      status: 0,
      title: "Can't reach the server",
      detail: 'Check the API is running, then try again. Resubmitting the same reference is safe.',
    });
  }
  const body = typeof err.error === 'object' && err.error ? err.error : {};
  return throwError(() => <ApiProblem>{ status: err.status, title: err.statusText, ...body });
}
