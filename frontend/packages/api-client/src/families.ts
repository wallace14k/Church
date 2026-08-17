import type { ApiClient } from './client';
import type { MemberStatus } from './members';

/** Espelha `FamilyEndpoints` no backend. */

export interface Family {
  readonly id: string;
  readonly name: string;
  readonly memberCount: number;
}

export interface FamilyMember {
  readonly id: string;
  readonly fullName: string;
  readonly status: MemberStatus;
}

export interface FamilyDetail {
  readonly id: string;
  readonly name: string;
  readonly members: readonly FamilyMember[];
}

export function listFamilies(client: ApiClient, signal?: AbortSignal): Promise<readonly Family[]> {
  return client.request<readonly Family[]>('/api/v1/families', {
    ...(signal ? { signal } : {}),
  });
}

export function getFamily(client: ApiClient, id: string): Promise<FamilyDetail> {
  return client.request<FamilyDetail>(`/api/v1/families/${id}`);
}

export function createFamily(client: ApiClient, name: string): Promise<Family> {
  return client.request<Family>('/api/v1/families', { method: 'POST', body: { name } });
}
