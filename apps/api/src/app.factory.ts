import "reflect-metadata";
import { NestFactory } from "@nestjs/core";
import type { INestApplication } from "@nestjs/common";
import { AppModule } from "./app.module.js";

/** Krijon aplikacionin Nest pa e nisur dëgjimin (i përdorshëm nga main dhe teste). */
export async function createApp(): Promise<INestApplication> {
  return NestFactory.create(AppModule, { logger: false });
}
