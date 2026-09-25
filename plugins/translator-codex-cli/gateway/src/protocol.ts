export interface Model {
  id: string;
  efforts: string[];
  session: boolean;
  context_tokens: number | null;
  output_tokens: number | null;
}

export interface Task {
  id: string;
  lease_token: string;
  state: string;
  prompt: string;
  schema: object;
  model: string;
  effort: string | null;
  session: string | null;
}

export interface Result {
  raw: string;
  completion: "completed" | "incomplete" | "truncated" | "failed" | "cancelled" | "uncertain";
  session: string | null;
}

export interface Registration {
  server_url: string;
  client_instance_id: string;
  code: string;
}

export interface Device {
  server_url: string;
  client_instance_id: string;
  installation_id: string;
  device_token: string;
  gateway_id?: string;
}
