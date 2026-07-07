import { Module } from "@nestjs/common";
import { AppController } from "./app.controller.js";
import { PocController } from "./poc/poc.controller.js";

@Module({
  controllers: [AppController, PocController],
})
export class AppModule {}
